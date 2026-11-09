import struct, sys, os, json

MAGIC = 0x5A6F12E1

def read_fstring(f):
    n = struct.unpack('<i', f.read(4))[0]
    if n == 0:
        return ''
    if n > 0:
        data = f.read(n)
        return data.rstrip(b'\x00').decode('utf-8', 'replace')
    data = f.read(-n * 2)
    return data.decode('utf-16-le', 'replace').rstrip('\x00')

def read_index(path):
    st = os.stat(path).st_size
    with open(path, 'rb') as f:
        # footer: search magic in last 512 bytes
        f.seek(max(0, st - 512))
        tail = f.read()
        i = tail.rfind(struct.pack('<I', MAGIC))
        if i < 0:
            raise RuntimeError('no pak magic')
        f.seek(st - 512 + i)
        magic, version = struct.unpack('<II', f.read(8))
        index_offset, index_size = struct.unpack('<qq', f.read(16))
        index_hash = f.read(20)
        f.seek(index_offset)
        index_data = f.read(index_size)
    print(f'{os.path.basename(path)}: version={version} index={index_offset} size={index_size}')
    import io
    g = io.BytesIO(index_data)
    mount = read_fstring(g)
    num = struct.unpack('<i', g.read(4))[0]
    entries = []
    for _ in range(num):
        name = read_fstring(g)
        off, size, usize = struct.unpack('<qqq', g.read(24))
        method = struct.unpack('<I', g.read(4))[0]
        h = g.read(20)
        nblocks = 0
        blocks = []
        if version >= 3:
            nblocks = struct.unpack('<i', g.read(4))[0]
            for _b in range(nblocks):
                bs, be = struct.unpack('<qq', g.read(16))
                blocks.append((bs, be))
        encrypted = 0
        if version >= 4:
            encrypted = g.read(1)[0]
        entries.append(dict(name=name, offset=off, size=size, usize=usize,
                            method=method, blocks=blocks, encrypted=encrypted))
    return dict(path=path, mount=mount, version=version, entries=entries)

if __name__ == '__main__':
    p = sys.argv[1]
    idx = read_index(p)
    e = idx['entries']
    print('mount:', idx['mount'])
    print('count:', len(e))
    print('encrypted:', sum(x['encrypted'] for x in e))
    print('compressed:', sum(1 for x in e if x['method'] != 0))
    for x in e[:5]:
        print('  ', x['name'], x['size'], x['usize'], x['method'])
    out = sys.argv[2] if len(sys.argv) > 2 else None
    if out:
        with open(out, 'w', encoding='utf-8') as fh:
            for x in e:
                fh.write(f"{x['offset']}\t{x['size']}\t{x['usize']}\t{x['method']}\t{x['encrypted']}\t{x['name']}\n")
        print('wrote', out)
