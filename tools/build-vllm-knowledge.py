"""Build a source-pinned learning graph delta; never write the user's archive.

Python standard library only. Sources are read with git show, not imported.
Use --snapshot graph.json --repos <parent-of-two-repos> --out <directory>.
"""
import argparse
import ast
import copy
import hashlib
import json
import pathlib
import re
import subprocess

COMMITS = {'vllm': 'c021a8b0a9049c39db7142823126b1c28a4fb85b',
           'vllm-ascend': 'e4d533d33211f0696bcdf65eda47245d52e428c5'}
DATE = '2026-11-13'
PIN = re.compile(r'https://github.com/vllm-project/(vllm(?:-ascend)?)/blob/([0-9a-f]{40})/([^\s#]+)#L(\d+)(?:-L(\d+))?')


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--snapshot', type=pathlib.Path, required=True)
    ap.add_argument('--repos', type=pathlib.Path, required=True)
    ap.add_argument('--out', type=pathlib.Path, required=True)
    args = ap.parse_args()
    graph = next(g for g in json.loads(args.snapshot.read_text(encoding='utf-8-sig'))['Topics']
                 if g['Id'] == 'vllm-ascend-source')
    original = {n['Id']: n for n in graph['Nodes']}
    cache = {}
    manifest = {}

    def source(repo, path, sha=None):
        sha = sha or COMMITS[repo]
        key = (repo, sha, path)
        if key not in cache:
            raw = subprocess.check_output(['git', '-C', str(args.repos / repo), 'show', f'{sha}:{path}'])
            cache[key] = raw.decode('utf-8').splitlines()
            manifest['/'.join(key)] = {'Repository': repo, 'Commit': sha, 'File': path,
                                      'SHA256': hashlib.sha256(raw).hexdigest(), 'Lines': len(cache[key])}
        return cache[key]

    def location(repo, path, symbol):
        lines = source(repo, path)
        obj = ast.parse('\n'.join(lines))
        for name in symbol.split('.'):
            obj = next(n for n in obj.body if getattr(n, 'name', None) == name)
        return obj.lineno, obj.end_lineno

    def url(repo, path, a, b):
        return f'https://github.com/vllm-project/{repo}/blob/{COMMITS[repo]}/{path}#L{a}-L{b}'

    def snippet(repo, path, a, b):
        lines = source(repo, path)
        assert 1 <= a <= b <= len(lines)
        # Keep contiguous verbatim excerpts; full function remains available via link.
        end = min(b, a + 39)
        return f'# {path} L{a}-L{end}\n' + '\n'.join(lines[a-1:end])

    changes = {}
    findings = {}

    def fix(id, note, **fields):
        changes.setdefault(id, {'Id': id}).update(fields)
        findings.setdefault(id, []).append(note)

    fix('entry-async-llm', '删除“Ascend 不改这一层/改动都在 EngineCore 内部”的无证据全称判断。',
        Summary='AsyncLLM 是异步生成入口：把输入转换为 EngineCoreRequest，登记每个请求的结果队列，再通过 EngineCoreClient 提交。这里展示多进程客户端路径；Ascend 插件还会应用进程级补丁，不能据此断言它绝不影响前端。',
        Approach=original['entry-async-llm']['Approach'].split('关键结论：')[0] + '边界：这是本提交的 AsyncLLM 提交流程，不是对 Ascend 全部补丁影响范围的证明；EngineCoreClient 另有 InprocClient，不能把所有入口都说成跨进程。')
    fix('engine-core-step', '明确 step 是非 batch_queue 路径；调度器替换取决于配置。',
        Approach=original['engine-core-step']['Approach'].replace('（Ascend 在此处换成自己的调度器）', '（选择配置指定的调度器；Ascend 仅在对应功能启用时改写该配置）') + '\n适用范围：本文解释 step；batch_queue 非空时选择 step_with_batch_queue，不能把本节点当作所有异步执行的完整时间线。')
    fix('scheduler-schedule', '统一 token 进度不等于代码完全不区分 prefill/decode；缓存登记位置不作排他判断。',
        Summary='调度器按请求的已计算 token 与目标 token 进度分配本轮预算，并申请 KV 缓存槽位。预填充是处理提示词，解码是继续生成；统一调度抽象并不消除节流、统计和设备执行中的阶段差异。',
        Approach=original['scheduler-schedule']['Approach'].replace('前缀缓存登记不在 schedule 内，而在同类', '输出确认后的前缀缓存登记可在同类').replace('的 L2412。', '的 L2412 找到；此处不声称所有登记路径都只发生在这里。'))
    fix('model-runner-execute', '前向与采样不是同一个函数内的无条件连续调用。',
        Summary='GPUModelRunner 把 SchedulerOutput 整理成输入批次、缓存块表和注意力元数据，并执行模型。最后一个流水线阶段可保存 execute_model_state，供单独的 sample_tokens 使用；Ascend 的 V2 Runner 继承并覆盖部分行为。',
        Approach=original['model-runner-execute']['Approach'] + '\n边界：这里的 execute_model 与 sample_tokens 是两个接口，不是前者必然直接调用后者；EngineCore.step 在执行结果为 None 时另行调用采样。Ascend 也改写 execute_model、采样、KV connector 等流程，差异不局限于三个组件。')
    fix('worker-gpu', 'NPUWorker 与 GPU Worker 同属 WorkerBase，不能把 NPU 选择描述成覆写 GPU _make_model_runner。',
        Approach=original['worker-gpu']['Approach'].replace('——Ascend 的 NPUWorker 正是在这里换成 NPUModelRunner。', '。Ascend 的 NPUWorker 另行继承 WorkerBase，在自己的 init_device 中直接创建 NPUModelRunner；它不是覆盖本类 _make_model_runner 的子类。'))
    fix('executor-boundary', '缩小“差别全在 Executor”与单一排错入口的过度概括。',
        Summary='EngineCore 接收并持有 Executor 实例，通过执行器接口派发模型执行、采样和缓存初始化。Executor 选择影响进程与 RPC 组织方式；平台、Worker 和启动参数也参与运行拓扑。',
        Approach=original['executor-boundary']['Approach'].replace('因此「某平台启动方式不同」应查 executor 选择（distributed_executor_backend），不是 engine 代码。', '排查启动方式时先检查 distributed_executor_backend，再检查平台配置、Worker 与 EngineCore 启动路径。'))
    registry = 'vllm/model_executor/models/registry.py'
    fix('model-registry-table', '实质错误：_ModelInfo 保存能力信息；module_name/class_name 属于 _LazyRegisteredModel。模型支持也不只依赖注册表。',
        Summary='注册表把架构名称映射到模型实现；懒注册对象保存模块路径和类名，能力检查结果保存在 _ModelInfo。注册成功只代表能够解析入口，不保证硬件、权重格式和算子支持。',
        Approach='输入是模型架构名称或插件注册项，输出是已注册的模型描述。_LazyRegisteredModel L1054-L1058 保存 module_name/class_name；load_model_cls L1188-L1190 使用 importlib 导入并取出类。inspect_model_cls L1155-L1186 优先读取基于模块 hash 的能力缓存，未命中才在子进程加载并检查模型。_ModelInfo L946 起保存 architecture、supports_lora、supports_pp、supports_multimodal 等能力字段，不保存模块路径。register_model 同时支持类对象和 module:class 字符串。',
        Sources=original['model-registry-table']['Sources'] + '\n' + url('vllm', registry, 946, 974) + '\n' + url('vllm', registry, 1155, 1190))
    fix('model-registry', '区分注册表方法与模块级懒加载函数，补充实际导入/能力检查证据。',
        Approach='resolve_model_cls L1494-L1533 根据 model_impl 和 architectures 选择原生或 transformers/terratorch 路径，返回模型类及架构名。注册表的 _try_load_model_cls 方法委托模块级缓存函数；模块级函数 L1194-L1205 先调用 current_platform.verify_model_arch，再调用 model.load_model_cls。懒对象的 load_model_cls L1188-L1190 才真正 import 模块；inspect_model_cls L1155-L1186 可读取能力缓存，未命中时在子进程检查，以减少主进程过早初始化 CUDA 的风险。解析到类不等于权重已装载或推理可运行。',
        Sources=original['model-registry']['Sources'] + '\n' + url('vllm', registry, 1155, 1205))
    fix('legacy-310p', '兼容表未实现 MLA/SFA 专项不等于整仓库或硬件全面不支持；名称不作为弃用判断。',
        Summary='310P 的兼容后端在当前选择表中只有普通注意力项，并有特定 KV 布局。此处没有独立 MLA/SFA 表项；该局部事实不能推出整个硬件或其他路径完全不支持，也不表示 310P 已弃用。',
        Approach=original['legacy-310p']['Approach'] + '\n范围：这是兼容后端选择表与缓存布局的静态观察，未验证任何具体模型在 310P 的支持矩阵。')
    fix('compile-backend', 'compute_hash 是编译器贡献的因素，不足以证明整个缓存键只由 Ascend 决定。',
        Summary='AscendCompiler 接收 FX 图与示例输入，按 enable_npugraph_ex 选择 npugraph_ex_compile 或 fusion_pass_compile，并提供包含 torch_npu 版本等因素的编译器 hash。这个 hash 不等同于上游完整缓存键。')
    fix('eplb', 'HIXL 会话存在不证明所有配置都使用 HIXL；旧目录存在不证明当前路径或弃用状态。',
        Summary='EPLB（专家并行负载均衡）根据专家负载调整放置和路由。V2 使用 AscendEPLBController 与 AscendEplbState，另有 eplb/ 下的子进程与 D2D 实现；具体通信器和迁移路径取决于配置，不能把两套实现混为一谈。')
    fix('engine-core-kv-init', '显存测量和预热都有条件分支，不写成每次启动必经。',
        Summary='EngineCore 在构造调度器前收集 Worker 的 KVCacheSpec，并结合可用缓存内存生成各 Worker 与调度器的 KVCacheConfig，再通过 Executor 初始化缓存。是否重新测量内存和执行预热取决于启动分支；无 KV 缓存模型另行处理。')

    definitions = [
      # id, module, repository, file, symbol, title, tier, summary, approach, question
      ('engine-core-request-struct','entry','vllm','vllm/v1/engine/__init__.py','EngineCoreRequest','EngineCoreRequest：前端与引擎的数据契约','CORE',
       'EngineCoreRequest 是 msgspec.Struct 请求对象，携带请求 ID、输入 token/嵌入、多模态特征及采样或池化参数。它负责传递请求数据，不负责调度或执行模型。',
       '类声明使用 array_like=True、omit_defaults=True、gc=False。prompt_token_ids 在类型检查时标作 list，运行时声明为 ndarray；注释区分进程间帧视图和进程内列表。client_index 用于把输出送回原客户端，external_req_id 保存外部请求标识；params 属性优先返回 sampling_params，否则要求 pooling_params 存在。输入转换由 InputProcessor 完成，消息编码由客户端负责；本类不是 dataclass。',
       'request_id、external_req_id、client_index 各解决什么问题？为什么不能把请求对象当成运行中请求的全部状态？'),
      ('input-process','entry','vllm','vllm/v1/engine/input_processor.py','InputProcessor.process_inputs','InputProcessor：校验并组装请求','CORE',
       '输入处理器接收 EngineInput 或旧式原始提示词及参数，检查任务、LoRA、设备和输入限制，返回 EngineCoreRequest。它属于前端预处理，不执行模型前向。',
       '已带 type 的 EngineInput 直接进入校验；旧式原始 prompt 经 renderer.render_cmpl 转换，源码将直接传原始 prompt 标为 deprecated。随后执行 current_platform.validate_request 和模型输入检查，按位置组织多模态特征，最终创建 EngineCoreRequest。token 是分词器产生的整数编号；多模态特征不是可以随意拼接的普通文本 token。',
       '为什么已经渲染的 EngineInput 与原始字符串走不同路径？非法 data_parallel_rank 在哪个阶段被拒绝？'),
      ('client-ipc','entry','vllm','vllm/v1/engine/core_client.py','AsyncMPClient._send_input','AsyncMPClient：编码并提交跨进程消息','CORE',
       '多进程异步客户端把请求类型和编码后的请求组成消息，再交给消息发送方法。IPC 指进程间通信；这是前端提交数据的边界，不是直接执行 EngineCore.step。',
       '_send_input 接收 EngineCoreRequestType、请求和可选 engine 标识；未指定 engine 时使用 core_engine。encoder.encode 的结果与 request_type.value 组成消息，由 _send_input_message 发送并返回可等待对象。另有 InprocClient，因此本节点结论只覆盖 AsyncMPClient。',
       '这里的 Awaitable 能否证明推理已经完成？发送请求和等待模型结果分别发生在哪里？'),
      ('request-runtime','cache','vllm','vllm/v1/request.py','Request.from_engine_core_request','Request：引擎内的请求状态入口','A',
       '引擎把传输对象 EngineCoreRequest 转成 Request，后者承载请求在调度中的状态。转换时把请求字段与块 hash 计算器交给构造函数。',
       'from_engine_core_request 是类方法，显式转交 client_index、prompt、参数、多模态、优先级和会话等字段，并接收 block_hasher 与可选 block_hashes。传输对象和内部状态对象有不同职责，不能把本转换当成模型执行或 token 生成。',
       '为什么前端请求与调度器中的 Request 分成两种对象？block_hasher 从哪里作为参数传入？'),
      ('request-status','cache','vllm','vllm/v1/request.py','RequestStatus','RequestStatus：等待、运行、抢占与结束','A',
       '请求状态枚举区分普通等待、等待语法、等待远程 KV、流式输入等待、运行、抢占及多种结束原因。抢占表示暂时退出执行，不等于请求已完成。',
       'is_finished 使用 status > PREEMPTED 判断终态；枚举顺序因此具有实现意义。FINISHED_STOPPED、LENGTH_CAPPED、ABORTED、ERROR 等分别表达停止条件、长度上限、取消和错误。状态声明只说明合法状态集合；具体转换时机需追踪调度器赋值，不能从枚举顺序推断运行顺序。',
       'PREEMPTED 为什么不是 finished？若把一个非终态加在 PREEMPTED 后会怎样？'),
      ('scheduler-output','engine','vllm','vllm/v1/core/sched/output.py','SchedulerOutput','SchedulerOutput：本轮执行计划','CORE',
       '调度器输出的执行计划告诉 Worker 本轮算哪些请求、各算多少 token，以及缓存与请求状态有哪些变化。它不是模型输出，也不是最终生成文本。',
       'scheduled_new_reqs 携带首次执行所需的数据；scheduled_cached_reqs 对已在 Worker 缓存的请求发送增量。num_scheduled_tokens 按请求记录预算，total_num_scheduled_tokens 是总量。finished_req_ids 通知 Worker 清理状态；草稿 token、encoder 输入、KV connector 元数据和新块清零列表支持可选路径。',
       '为什么要把首次请求与缓存请求分开？本轮 token 预算是否等于本轮返回给用户的 token 数？'),
      ('kv-prefix-lookup','cache','vllm','vllm/v1/core/kv_cache_manager.py','KVCacheManager.get_computed_blocks','前缀缓存查询：找可复用的计算结果','CORE',
       '前缀缓存保存已计算输入对应的 KV，使相同前缀的后续请求少做重复计算。查询返回缓存块、可复用 token 数和共享前缀边界；命中并不等于整次请求不用计算。',
       '先检查 prefix_cache_lookup_enabled；禁止读取时返回空块和零计数。再按请求给出的最大命中长度查询 coordinator.find_longest_cache_hit。源码注释说明：为获得 logits，全部提示词命中时仍需重新计算末尾 token；块对齐可能放大重算范围。稀疏保留组还需要 shared_prefix_boundary，防止后续重用断开。',
       '两个提示词完全相同时，为什么仍可能看到计算？缓存块、token 命中数和边界各有什么用途？'),
      ('kv-pool-allocate','cache','vllm','vllm/v1/core/block_pool.py','BlockPool.get_new_blocks','BlockPool：领取可用缓存块','CORE',
       'BlockPool 管理缓存块的空闲队列与引用计数。get_new_blocks 按数量领取空闲块，必要时移除旧缓存索引，并把块标记为正在被使用。',
       '申请数超过空闲数时抛错；正常路径从队头取块。启用缓存时，重新使用带 hash 的空闲块会调用 _maybe_evict_cached_block；随后要求 ref_cnt 为零并改成一。这里只管理块对象及索引，不执行设备上的注意力，也不直接创建一份新 GPU 张量。',
       '一个块已进入空闲队列，为什么仍可能带 block_hash？再次领取时为什么需要处理旧索引？'),
      ('kv-pool-free','cache','vllm','vllm/v1/core/block_pool.py','BlockPool.free_blocks','BlockPool：引用计数与回收顺序','S',
       '归还缓存块首先减少引用计数；只有降到零的块才重新进入空闲队列。请求释放块并不意味着共享缓存立刻消失。',
       '空块哨兵跳过；属于其他池的块转交对应池处理。无 hash、禁用缓存或指定 evict_first 的块放队头优先复用；仍有缓存 hash 的块放队尾，形成面向缓存的 LRU 淘汰顺序。该函数不保证在飞设备写入已结束；调度器的延迟释放机制负责这个上层时序条件。',
       '两个请求共享一个块，结束其中一个后能否立刻复用这个块？延迟释放与引用计数解决的是同一个问题吗？'),
      ('sample-token','execution','vllm','vllm/v1/worker/gpu/sample/sampler.py','Sampler.__call__','Sampler：从 logits 得到 token','CORE',
       'logits 是模型为词表中候选 token 计算的分数。Sampler 接收 logits 和本轮输入批次，按请求参数取样，并返回 token、概率统计和有效采样数量。',
       '__call__ 先得到请求与位置映射，再调用 sample；需要 logprobs 时计算相应分数。尚未完成分块预填充的请求不产生有效输出 token，不能把前向 token 数当作生成数量。普通路径 sampled_token_ids 整理为二维，每请求一列；num_sampled 才反映该请求本轮有效产出。温度、惩罚和 top-k/top-p 的实现继续从 sample 追踪。',
       '为什么执行了前向却没有可返回的新 token？sampled_token_ids 的形状与 num_sampled 有何区别？'),
      ('spec-verify','execution','vllm','vllm/v1/worker/gpu/spec_decode/rejection_sampler.py','RejectionSampler.__call__','推测解码：验证草稿并统计接受数量','S',
       '推测解码先提出多个候选 token，再由目标模型验证。RejectionSampler 接收目标 logits、输入批次和可选草稿 logits，返回接受后的 token 与拒绝数量。',
       '__call__ 通过 gather_draft_sampled 取草稿与位置，调用 _verify_in_chunks 分块验证，再用 get_num_sampled_and_rejected 计算有效产出与拒绝量，组装 SamplerOutput。这里核对的是验证入口和数据流；接受概率算法在下层，不能据入口断言所有配置都采用同一算法或一定提速。',
       '草稿数为四是否保证一次返回五个 token？性能判断还需要记录哪些接受率和执行成本？'),
      ('output-process','entry','vllm','vllm/v1/engine/output_processor.py','OutputProcessor.process_outputs','OutputProcessor：把引擎输出交给请求方','CORE',
       '输出处理器按 request_id 找到前端请求状态，把引擎产出的 token 处理成 RequestOutput，写入异步请求队列或返回结果列表，并收集需要通知引擎中止的请求。',
       '没有对应状态的输出会被忽略，例如请求已被取消。正常路径更新统计、反分词与概率信息，再用 make_request_output 创建结果；有 queue 时调用 put，没有 queue 时加入返回列表。命中前端停止条件而引擎尚未结束时，需要把 ID 放进 reqs_to_abort；不是只停止前端显示。',
       '为什么检测到停止字符串后还要通知引擎 abort？异步与同步调用者在哪里分流？'),
      ('detokenize-stop','entry','vllm','vllm/v1/engine/detokenizer.py','BaseIncrementalDetokenizer.update','反分词与停止字符串：token 变成文本','A',
       '反分词把新生成的 token 编号逐步转换成文本，并检查停止字符串。停止字符串可能跨多个 token，不能简单用单个 token 编号判断。',
       'update 对每个新 token 调用 decode_next，更新累计文本并记录 token 对应的字符边界。满足 min_tokens 后调用 check_stop_strings；命中时按 include_stop_str_in_output 决定文本保留范围，还裁掉同一轮多生成的后续 token。返回值是匹配到的停止字符串或 None，不是新文本本身。',
       '为什么要保留 token_offsets？update 返回 None 能否说明没有产生文字？'),
      ('structured-grammar','engine','vllm','vllm/v1/structured_output/__init__.py','StructuredOutputManager.grammar_init','结构化输出：准备语法约束','S',
       '结构化输出通过语法限制可生成的 token，帮助请求满足 JSON 等结构。grammar_init 根据请求选择的后端创建语法对象，必要时异步编译。',
       '没有 structured_output_request 时直接返回；同一引擎已有后端时拒绝不同后端混用。首次使用按 xgrammar、guidance、outlines 或 lm-format-enforcer 初始化后端。异步路径把 _create_grammar 提交给 executor，结果作为 Future 保存；等待语法就绪与后续 bitmask 生成是另一些步骤。语法约束不保证内容事实正确。',
       '为什么准备语法可能让请求进入等待状态？符合 JSON 语法是否等于回答内容正确？'),
      ('ascend-attn-contract','attention','vllm-ascend','vllm_ascend/attention/attention_v1.py','AscendAttentionBackend','AscendAttentionBackend：实现类与缓存布局契约','CORE',
       '注意力后端描述使用哪个计算实现、哪个元数据构造器以及 KV 缓存布局。它把平台选择结果接到实际注意力执行，不在类选择阶段完成注意力计算。',
       'get_impl_cls 和 get_builder_cls 在 DCP 启用时选择对应专用实现，否则使用普通实现。get_kv_cache_shape 对 LBHNC/HND 返回 (2, blocks, kv_heads, block_size, head_size)，其他布局返回 (2, blocks, block_size, kv_heads, head_size)。最前面的 2 区分 K 与 V；支持的 kernel block size 在此返回 [128]，不能推广到全部 Ascend 注意力后端。',
       '为什么交换 block_size 和 kv_heads 的维度顺序会影响算子读取？后端类和实现类分别负责什么？'),
      ('ascend-attn-metadata','attention','vllm-ascend','vllm_ascend/attention/attention_v1.py','AscendAttentionMetadataBuilder.build','注意力元数据：块表、位置与有效长度','CORE',
       '元数据构造器把通用批次信息转成 AscendMetadata，告诉注意力实现每个请求读哪些缓存块、把新 KV 写到哪些位置、哪些 token 真正有效。',
       'build 取得 block_table_tensor、slot_mapping 与 query_start_loc，区分 decode/prefill 的请求和 token 数，选择序列长度并构造 attention mask。返回对象带 block_tables、slot_mapping、seq_lens、actual_seq_lengths_q 和实际 token 数。图执行可能包含补齐 token，因此真实长度与张量容量不可混为一谈。PCP 专用元数据另行 finalize。',
       'block_tables 与 slot_mapping 分别面向读取还是写入？补齐 token 为什么不能被当成真实请求内容？'),
      ('ascend-attn-forward','attention','vllm-ascend','vllm_ascend/attention/attention_v1.py','AscendAttentionBackendImpl.forward','Ascend 注意力前向：写缓存并计算输出','CORE',
       '普通 Ascend 注意力实现接收 Q/K/V、KV 缓存、元数据和输出缓冲区，先准备缓存视图、按需写入新的 K/V，再选择注意力计算路径并写回结果。',
       '必须提供 output；本实现不支持 output_scale/output_block_scale 融合输出量化，缺元数据时返回零填充结果。存在新 K/V 时按 PCP 布局选择 _reshape_and_cache_pcp 或 reshape_and_cache；pooling 的双向/encoder-only 分支调用 _forward_encoder_attention，其余调用 forward_impl。此处不推断 torch_npu/CANN 底层 kernel 的具体实现或性能。',
       '为什么 DecodeOnly 分支可能没有新 key/value 但仍需访问缓存？本函数的量化限制能否代表全部后端？'),
      ('ascend-mla-forward','attention','vllm-ascend','vllm_ascend/attention/mla_v1.py','AscendMLAImpl.forward','MLA 前向：压缩缓存与预填充/解码分流','S',
       'MLA 是多头潜在注意力：使用压缩表示组织 KV。该前向接收 hidden_states、缓存和元数据，分别处理解码与预填充，再进行输出投影。',
       '融合缓存张量通过切片视图拆成 NoPE/RoPE 两部分；num_decode_tokens 划分批次前后段。预处理按设备能力、缓存形式及并行条件选择分支，解码使用外部 FlashMLA 或 _forward_decode，预填充进入 _forward_prefill；结果填入 o_proj_input，再调用 o_proj 并按需保存 KV 层。这里核对数据组织与分支，不声称所有模型都会选中这条 MLA 实现。',
       '为什么同一批次里可能同时有 decode 和 prefill？融合缓存切片是否一定分配新的缓存内存？'),
      ('ascend-sample','npu','vllm-ascend','vllm_ascend/worker/v2/model_runner.py','NPUModelRunner.sample_tokens','Ascend 采样包装：复用上游与条件限制','S',
       'NPU Runner 的 sample_tokens 在调用上游采样前处理 LM-head 张量并行限制与补齐信息，并在完成后结束本轮调试数据收集。LM-head 把隐状态映射为词表 logits。',
       '启用 lmhead TP 且请求 prompt logprobs 或固定 token prompt scoring 时，此路径抛出 NotImplementedError；源码注释解释未补齐的额外 compute_logits 会使集合通信不一致。其他情况准备 _lmhead_pad_size，调用 super().sample_tokens，再清理补齐状态并 _finalize_dump_data。版本适配与拒绝采样分流还位于本类其他方法；不是另写了一套完全独立的 NPU 采样器。',
       '为什么 prompt logprobs 会影响 LM-head 并行的集合通信？super().sample_tokens 说明了什么复用关系？'),
    ]
    placements = []
    for id, module, repo, path, symbol, title, tier, summary, approach, question in definitions:
        a, b = location(repo, path, symbol)
        pin = url(repo, path, a, b)
        node = dict(Id=id, Title=title, Tier=tier, Summary=summary,
                    Approach=approach + '\n证据范围：固定提交的静态源码核对；未运行 GPU/NPU 推理，不能据此认定两个仓库兼容或用户已掌握。',
                    Code=snippet(repo, path, a, b), Sources=pin,
                    Version=f'{repo}@{COMMITS[repo]}', Status=f'源码静态复核 · {DATE} · 未运行验证',
                    Learning=dict(CodeUrl=pin, Goal='能说明本节点的输入、输出、职责与条件边界，并从源码定位关键分支。', Question=question, Evidence=[]))
        changes[id] = node
        placements.append(dict(NodeId=id, ModuleId=module))
        if id in original:
            findings[id] = ['补齐实际 msgspec.Struct 定义、字段与传输边界，替换仅使用点证据。']

    edges = []
    def edge(f, t, label, evidence):
        edges.append(dict(From=f, To=t, Label=label, Evidence=evidence))

    edge('entry-async-llm','input-process','调用输入处理','async_llm.py L497/L514；输入处理器返回 EngineCoreRequest。')
    edge('input-process','engine-core-request-struct','创建请求数据',changes['input-process']['Sources'] + '：return EngineCoreRequest(...)。')
    edge('entry-async-llm','client-ipc','经客户端提交（多进程配置）','AsyncLLM._add_request L598 调用客户端 add_request_async；AsyncMPClient L1374-L1377 转交 ADD 消息。')
    edge('client-ipc','engine-core-request-struct','编码的请求载荷',changes['client-ipc']['Sources'] + '；消息类型与 encoder.encode(request) 组成传输消息。不是调用请求类的执行方法。')
    edge('engine-core-request-struct','request-runtime','字段转换',changes['request-runtime']['Sources'])
    edge('request-runtime','request-status','状态词汇（学习关系）','RequestStatus 声明内部请求的状态集合；不是枚举顺序对应的调用链。')
    edge('scheduler-schedule','scheduler-output','返回执行计划','Scheduler.schedule 返回 SchedulerOutput；字段定义：' + changes['scheduler-output']['Sources'])
    edge('scheduler-output','model-runner-execute','经 Executor/Worker 传入','EngineCore.step L728-L731 经执行器派发；GPU Worker L1485-L1487 转交，非 SchedulerOutput 自行调用模型。')
    edge('kv-prefix-lookup','kv-prefix-cache','查询与登记（概念配对）','get_computed_blocks 查询已计算缓存；cache_blocks 登记可复用前缀。两者职责配对，不是该查询方法直接调用登记方法。')
    edge('kv-block-alloc','kv-pool-allocate','调用领取块','SingleTypeKVCacheManager.allocate_new_blocks L411 调用 block_pool.get_new_blocks。')
    edge('scheduler-preempt','kv-pool-free','经管理器/延迟队列回收','Scheduler._free_request_blocks L3171-L3189；不保证抢占发生时立刻返回块池。')
    edge('kv-pool-allocate','kv-pool-free','块生命周期（学习关系）','领取时 ref_cnt 由 0 变 1；归还时递减，归零才进入空闲队列。不是两函数直接互调。')
    edge('sample-token','spec-verify','普通采样与草稿验证（对照）','两者都返回 SamplerOutput；草稿验证额外消费 draft_logits 并计算拒绝量，不表示普通采样总会触发推测解码。')
    edge('entry-output-loop','output-process','调用处理批量输出','AsyncLLM._run_output_handler L919-L923 调用 output_processor.process_outputs。')
    edge('output-process','detokenize-stop','委托反分词与停止检测',changes['output-process']['Sources'] + '；process_outputs 调用请求 detokenizer.update。')
    edge('structured-grammar','engine-step-detail-grammar','准备约束的前置阶段（学习关系）','语法初始化先准备 grammar/Future；step 后续请求位掩码。非 grammar_init 直接调用 step。')
    edge('attn-backend-select','ascend-attn-contract','普通注意力类路径','platform.py L334-L336 返回 AscendAttentionBackend；其他模型/设备条件可选择别的类。')
    edge('ascend-attn-contract','ascend-attn-metadata','返回构造器类','AscendAttentionBackend.get_builder_cls L98-L103，DCP 条件下另选专用类。')
    edge('ascend-attn-contract','ascend-attn-forward','返回实现类','AscendAttentionBackend.get_impl_cls L90-L95，DCP 条件下另选专用类。')
    edge('ascend-attn-metadata','ascend-attn-forward','提供元数据契约','builder 返回 AscendMetadata；forward 接收 attn_metadata。连接者是执行路径，不是 builder 直接调用 forward。')
    edge('attn-backend-select','ascend-mla-forward','MLA 后端路径（间接）','platform.py L335 选择 AscendMLABackend，mla_v1.py get_impl_cls 返回 MLA 实现；不代表全部 Ascend 请求都使用 MLA。')
    edge('ascend-model-runner','ascend-sample','同类方法细分','NPUModelRunner.sample_tokens 是独立方法；不是 execute_model 无条件直接调用它。')
    edge('ascend-sample','sample-token','上游采样路径（间接）','NPUModelRunner.sample_tokens L345 调用 super；同类采样分流 L1170-L1192 根据草稿数量选 sampler/rejection_sampler。')
    edge('ascend-sample','spec-verify','存在草稿时的验证分支（间接）','Ascend model_runner.py L1170-L1192；0.33.0 与其他版本有参数差异，两个独立快照未做兼容验证。')

    # Repair evidence under the existing key; protocol matches From/To/Label.
    for e in graph['Edges']:
        if (e['From'], e['To']) == ('engine-core-kv-init', 'ascend-model-runner'):
            fixed = copy.deepcopy(e)
            fixed['Evidence'] = 'core.py L407 经 Executor.initialize_from_config 下发；NPUWorker.initialize_from_config 接收配置再初始化 Runner。是否跨进程取决于 Executor 拓扑，不能断言两层必在不同进程。'
            edges.append(fixed)
        if (e['From'], e['To']) == ('kv-block-alloc', 'kv-prefix-cache'):
            fixed = copy.deepcopy(e)
            fixed['Evidence'] = '学习前置：allocate_new_blocks 使用请求块表；检测 _partial_hit_reqs 后执行 CoW，并非此处把请求写入 _partial_hit_reqs。cache_blocks 再结合计算进度和 block_hashes 登记。'
            edges.append(fixed)

    # Validate every old and new pinned URL against the actual Git object.
    audit = []
    for n in graph['Nodes']:
        links = list(PIN.finditer(n.get('Sources','') + '\n' + (n.get('Learning') or {}).get('CodeUrl','')))
        assert links, n['Id']
        for m in links:
            repo, sha, path, a, b = m.groups()
            assert sha == COMMITS[repo], (n['Id'], sha)
            assert 1 <= int(a) <= int(b or a) <= len(source(repo, path, sha)), (n['Id'], m.group())
        audit.append(dict(Id=n['Id'], Result='修正/补证' if n['Id'] in findings else '保留（静态复核，限定既有证据范围）',
                          Findings=findings.get(n['Id'], ['来源定位及所摘代码与固定提交对应；未扩大为全仓库、算子运行或兼容性结论。']),
                          SourceLinks=list(dict.fromkeys(m.group() for m in links))))
    for n in changes.values():
        n['Status'] = f'源码静态复核 · {DATE} · 未运行验证'
        for m in PIN.finditer(n.get('Sources','')):
            repo, sha, path, a, b = m.groups()
            assert 1 <= int(a) <= int(b or a) <= len(source(repo,path,sha))
    ids = set(original) | set(changes)
    assert all(e['From'] in ids and e['To'] in ids and e['From'] != e['To'] for e in edges)
    modules = {m['Id'] for m in graph['Architecture']['Modules']}
    assert all(p['ModuleId'] in modules for p in placements)
    args.out.mkdir(parents=True, exist_ok=True)
    def write(name, value):
        (args.out/name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    write('graph-delta.json', dict(TopicId=graph['Id'], ExpectedRevision=graph['Revision'],
                                  Nodes=list(changes.values()), Edges=edges, NodeModules=placements))
    write('source-manifest.json', dict(CheckedOn=DATE, Commits=COMMITS, Files=list(manifest.values())))
    write('review.json', dict(CheckedOn=DATE, Scope='已有节点逐项静态复核、关键关系复核；非全仓逐行审计，非设备测试。',
                             Nodes=audit, NewNodes=[d[0] for d in definitions if d[0] not in original],
                             RelationReview='复核原有关系语义与关键边界；修正两条证据，新增关系显式区分调用、数据契约与学习关系。',
                             Limits=['两个仓库独立固定提交，未验证组合兼容性。', '未运行 GPU/NPU 或 CANN kernel；不提供未经测量的性能结论。',
                                     'PR 动机、全部量化/LoRA/PD connector/CP/MoE 算子细节仍需后续专题；不把教程最新页面当成固定版本事实。',
                                     '用户复述及 ReadingGuide 保留；原说明变化后旧 ReadingGuide 由应用标为可能过期。']))
    new_count = len([d for d in definitions if d[0] not in original])
    report = [f'vLLM / vLLM-Ascend 初始知识图谱复核与增补（{DATE}）',
              f'既有 {len(original)} 个节点逐项静态复核；修正或补证 {len(findings)} 个；新增 {new_count} 个；完成后 {len(ids)} 个。',
              '沿用已有模块、旅程和个人复述；无部署或推理实测。',
              '建议阅读：请求对象 → 输入处理 → 客户端 → EngineCore.step → SchedulerOutput → KV 查询/分配/回收 → Runner/采样 → 输出处理/停止。',
              'Ascend 支线：插件 → NPUPlatform → Worker/Runner → 注意力后端/元数据/前向 → MLA → 图执行 → MoE/EPLB。',
              '\n复核改动：']
    report += [f'- {id}: ' + '；'.join(notes) for id, notes in findings.items()]
    report += ['\n资料入口（背景材料；以固定源码为本版实现证据）：',
               'https://docs.vllm.ai/en/latest/design/arch_overview/',
               'https://docs.vllm.ai/en/latest/design/plugin_system/',
               'https://docs.vllm.ai/projects/ascend/en/main/developer_guide/Design_Documents/index.html',
               'https://arxiv.org/abs/2309.06180',
               '\n后续：补充量化、LoRA、PD 传输与上下文并行的具体运行路径；读取算子规格与测试用例，再做匹配版本的设备验证。',
               '\n生成命令仅产生增量；graph-edit-result.json 同 RequestId 返回 applied 才表示存档已保存。']
    (args.out/'review.txt').write_text('\n'.join(report)+'\n', encoding='utf8')
    print(json.dumps(dict(Old=len(original), Corrected=len(findings), Added=new_count, Total=len(ids),
                          Patches=len(changes), Edges=len(edges), SourceFiles=len(manifest)), ensure_ascii=False))


if __name__ == '__main__':
    main()
