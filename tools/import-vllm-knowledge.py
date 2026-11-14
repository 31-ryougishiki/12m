"""Import a prepared delta via the running pet's revision-checked edit protocol.

No direct archive/snapshot mutation. Saves a private backup in --backup.
The live application must be running. A timed-out request is left for inspection.
"""
import argparse
import copy
import json
import pathlib
import shutil
import time
import uuid


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--delta', required=True, type=pathlib.Path)
    ap.add_argument('--knowledge', required=True, type=pathlib.Path)
    ap.add_argument('--backup', required=True, type=pathlib.Path)
    args = ap.parse_args()
    delta = read(args.delta)
    root = args.knowledge.resolve()
    backup = args.backup.resolve()
    backup.mkdir(parents=True, exist_ok=False)
    def topic():
        return next(g for g in read(root/'graph.json')['Topics'] if g['Id'] == delta['TopicId'])
    before = topic()
    assert before['Revision'] == delta['ExpectedRevision'], 'Snapshot changed: review the delta before retrying.'
    request_file, receipt_file = root/'graph-edit.json', root/'graph-edit-result.json'
    if request_file.exists():
        pending = read(request_file)
        receipt = read(receipt_file) if receipt_file.exists() else {}
        assert receipt.get('RequestId') == pending.get('RequestId'), 'Another edit is pending.'
    for name in ['graph.json', 'graph-edit.json', 'graph-edit-result.json']:
        if (root/name).exists():
            shutil.copy2(root/name, backup/name)
    archive = root.parent.parent/'archives.json'
    if archive.exists():
        shutil.copy2(archive, backup/'archives.json')
    nodes = delta['Nodes']
    receipts = []
    expected = copy.deepcopy(before)
    expected_revision = before['Revision']
    for start in range(0, len(nodes), 30):
        current = topic()
        assert current['Revision'] == expected_revision, 'Concurrent edit detected; stop without overwriting.'
        batch = nodes[start:start+30]
        batch_ids = {n['Id'] for n in batch}
        last = start+30 >= len(nodes)
        request = dict(RequestId=str(uuid.uuid4()), TopicId=delta['TopicId'],
                       ExpectedRevision=current['Revision'], Nodes=batch,
                       Edges=delta['Edges'] if last else [],
                       NodeModules=[p for p in delta['NodeModules'] if p['NodeId'] in batch_ids])
        if last and delta.get('Modules'):
            request['Modules'] = delta['Modules']
        (backup/f'request-{start//30+1}.json').write_text(json.dumps(request,ensure_ascii=False,indent=2),encoding='utf8')
        tmp = root/('graph-edit.'+request['RequestId']+'.tmp')
        tmp.write_text(json.dumps(request,ensure_ascii=False),encoding='utf8')
        tmp.replace(request_file)
        deadline = time.monotonic()+45
        while time.monotonic() < deadline:
            try:
                result = read(receipt_file)
                if result.get('RequestId') == request['RequestId']:
                    assert result.get('Status') == 'applied', result
                    receipts.append(result)
                    break
            except (FileNotFoundError, json.JSONDecodeError):
                pass
            time.sleep(1)
        else:
            raise TimeoutError('No matching receipt; request remains pending. Do not claim import succeeded.')
        current = topic()
        expected_revision = current['Revision']
        actual = {n['Id']: n for n in current['Nodes']}
        for patch in batch:
            for key,value in patch.items():
                if key == 'Learning':
                    for k,v in value.items():
                        assert actual[patch['Id']][key][k] == v, (patch['Id'],key,k)
                else:
                    assert actual[patch['Id']][key] == value, (patch['Id'],key)
        print(f'Applied batch {start//30+1}: {len(batch)} nodes; verified snapshot.', flush=True)
    after = topic()
    old = {n['Id']: n for n in before['Nodes']}
    new = {n['Id']: n for n in after['Nodes']}
    patches = {n['Id']: n for n in nodes}
    for id,n in old.items():
        for key,value in n.items():
            if key not in patches.get(id,{}):
                assert new[id][key] == value, ('Unedited field changed',id,key)
        assert (new[id].get('Learning') or {}).get('Reflection','') == (n.get('Learning') or {}).get('Reflection','')
        assert new[id].get('ReadingGuide') == n.get('ReadingGuide')
    assert before['Architecture']['Journey'] == after['Architecture']['Journey']
    module_patches = {m['Id']: m for m in delta.get('Modules', [])}
    after_modules = {m['Id']: m for m in after['Architecture']['Modules']}
    for m in before['Architecture']['Modules']:
        for key, value in m.items():
            if key == 'Nodes' and delta.get('NodeModules'):
                continue  # Membership changes are checked against placements below.
            assert after_modules[m['Id']][key] == module_patches.get(m['Id'], {}).get(key, value), ('Module changed unexpectedly', m['Id'], key)
    for m in module_patches.values():
        for key, value in m.items():
            assert after_modules[m['Id']][key] == value
    members = [id for m in after['Architecture']['Modules'] for id in m['Nodes']]
    assert len(members) == len(set(members)) == len(after['Nodes'])
    assert set(members) == set(new)
    for p in delta['NodeModules']:
        assert p['NodeId'] in next(m['Nodes'] for m in after['Architecture']['Modules'] if m['Id']==p['ModuleId'])
    for e in delta['Edges']:
        assert e in after['Edges']
    for e in before['Edges']:
        if not any((x['From'],x['To'],x['Label']) == (e['From'],e['To'],e['Label']) for x in delta['Edges']):
            assert e in after['Edges']
    # Independently read the persisted archive, not only its derived snapshot.
    book = read(archive)
    persisted = [g for s in book['Slots'] for g in s.get('DomainGraphs',[]) if g['Id']==delta['TopicId']]
    assert any(g['Nodes']==after['Nodes'] and g['Edges']==after['Edges'] and g['Architecture']==after['Architecture'] for g in persisted)
    summary = dict(Status='applied-and-read-back', Nodes=len(after['Nodes']), Edges=len(after['Edges']),
                   ExistingNodesPreserved=len(old), PersonalReflectionsPreserved=True, ReadingGuidesPreserved=True,
                   ModuleCoverageVerified=True, ArchiveReadBackVerified=True, Receipts=receipts,
                   Revision=after['Revision'])
    (args.delta.parent/'import-result.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps(summary,ensure_ascii=False))


if __name__ == '__main__':
    main()
