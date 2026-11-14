"""Prepare a clarity-only delta from the live graph; apply via graph edit protocol."""
import argparse
import copy
import json
from pathlib import Path


# Plain-language explanations stay within the pinned evidence already reviewed.
# Values: role, input, implementation steps, result and boundary.
EXPLANATIONS = {
'entry-async-llm': (
 'AsyncLLM 接收生成请求，为请求登记接收结果的队列，再把请求交给引擎客户端。它负责前端请求管理，模型计算由后面的引擎和设备执行层完成。',
 '调用者提供提示词、请求编号和生成参数；提示词可能已经完成渲染，也可能仍需分词。',
 'add_request 调用输入处理器，把输入整理成 EngineCoreRequest；检查准入条件后，登记输出队列，再调用 engine_core.add_request_async。请求要求多个结果时，会拆成子请求分别提交。',
 '返回用于等待结果的队列；请求通过客户端到达 EngineCore。多进程客户端使用 IPC（进程间通信），不是直接调用 step；Ascend 插件补丁可能影响多个进程，不能据此划定其全部作用范围。'),
'entry-output-loop': (
 '输出协程负责持续接收引擎结果，并交给输出处理器分发到每个请求的队列。它由 AsyncLLM 启动，是可暂停等待的异步任务，不是一个新设备工作进程。',
 'EngineCoreClient 返回一包 EngineCoreOutputs，其中包含多个请求的结果、时间戳等信息。',
 '首次 add_request 时启动 output_handler。协程等待 get_output_async，把结果列表按配置数量分组，逐组调用 output_processor.process_outputs；处理器返回待中止请求时，再通知客户端 abort。这里分组的是结果对象，不是把文本切成小段。',
 '每个请求的队列收到 RequestOutput，调用者再从队列取得结果。停止字符串由输出处理器检查；此协程负责把中止通知交回引擎。'),
'engine-core-step': (
 'EngineCore.step 负责组织一次推理迭代：请调度器安排工作，经执行器派发计算，取得结果后再请调度器更新请求状态。一个请求通常需要经历多次迭代。',
 '调度器中等待或正在运行的请求，以及当前缓存和执行状态。',
 '先检查是否还有请求；有请求时调用 schedule，得到本轮执行计划 SchedulerOutput。随后调用 Executor.execute_model，并取得 Future（稍后可获取结果的对象）；准备语法约束后等待结果。若执行结果为 None，再调用 sample_tokens。处理中止请求后，把计划和模型输出交给 update_from_output。',
 '返回按客户端编号分组的 EngineCoreOutputs，以及本轮是否安排了非零 token 的标志。step 不直接调用 Runner；中间经过 Executor 和 Worker。启用 batch_queue 时使用另一个入口 step_with_batch_queue。'),
'engine-core-kv-init': (
 '缓存初始化负责在启动时确定各设备需要多少 KV 缓存、如何组织这些缓存，并把配置交给 Worker。KV 缓存保存注意力计算中的键和值，供后续 token 复用。',
 'Worker 汇总的 KVCacheSpec（模型各层的缓存需求），以及可用于缓存的设备内存容量。',
 'EngineCore 先注册平台的缓存规格，再通过 Executor 收集需求和可用容量。缓存规划代码据此生成各 Worker 使用的 KVCacheConfig，以及调度器使用的缓存配置；随后通过 Executor.initialize_from_config 通知 Worker 初始化缓存。',
 'Worker 据配置准备设备缓存，Scheduler 据配置建立缓存管理器。重新测量内存、编译和预热都有条件分支；Ascend 的 builder 参与布局规划，并不代替 Worker 分配设备张量。'),
'scheduler-schedule': (
 'Scheduler.schedule 决定这一轮让哪些请求继续计算、各计算多少 token，并为这些计算申请 KV 缓存空间。token 是分词后的编号单位，本轮计算量不等于本轮生成的文字量。',
 '请求的已计算进度、目标进度、本轮 token 总预算、空闲缓存块，以及是否推迟部分预填充的条件。',
 '调度器用统一的 token 进度安排请求，向 KVCacheManager.allocate_slots 申请空间；空间不足时可能抢占其他请求，再整理 SchedulerOutput。预填充是处理提示词，解码是继续生成；统一进度表示法不代表代码完全不区分这两类工作。',
 'SchedulerOutput 经 EngineCore 和 Executor 交给 Worker。执行结束后，由同一 Scheduler 的 update_from_output 更新状态并登记可复用缓存；schedule 本身不完成模型前向。'),
'scheduler-preempt': (
 '抢占是让某个正在运行的请求暂时退出执行，以便重新安排有限的 KV 缓存空间。请求会回到等待队列，之后可能重新计算；抢占不是请求结束。',
 '需要暂时退出的 Request、当前时间，以及是否丢弃旧执行结果的标志。',
 '_preempt_request 请求释放 KV 块和多模态编码缓存，将状态改为 PREEMPTED，并重置已计算进度。若异步执行中仍有旧任务尚未返回，还要标记哪些结果已经过期。请求最终重新进入等待队列。',
 '调度器可在后续轮次重新选择该请求。尚未结束的设备任务可能仍写缓存，因此 _free_request_blocks 可延迟把块归还给 BlockPool；不能理解为调用抢占后所有块立即可复用。'),
'kv-block-alloc': (
 'SingleTypeKVCacheManager 管理一类缓存布局下的请求块表。它计算请求还缺多少缓存块，从 BlockPool 领取块，并记录该请求拥有的块。',
 '请求编号、需要容纳的 token 数、已有块表，以及是否命中了一个仍被其他请求共享的尾块。',
 '普通扩容用向上取整后的块数减去已有块数，得到还需领取的数量。部分前缀命中时，如果继续写入会修改共享尾块，就通过 CoW（写入前复制）建立该请求自己的块，然后继续补齐容量。',
 '更新后的请求块表描述数据放在哪些块中。上层由 KVCacheManager 及其 coordinator 转交请求；Scheduler 不直接操作本类，领取块也不等于设备已完成计算。'),
'kv-prefix-cache': (
 '前缀缓存登记把已经计算、允许被复用的 KV 块关联到输入前缀的 hash。hash 是用于查找相同前缀的摘要键；后续请求可据此找到可复用的计算结果。',
 '请求块表、已经确认的计算进度、输入前缀 hash，以及该类缓存的保留规则。',
 'cache_blocks 先检查缓存规格是否允许前缀复用，再用计算进度和已有 hash 限定可登记范围。对只保留部分历史状态的缓存组，还要选择能够支持后续继续计算的边界。FullAttentionManager 在符合条件时另行登记块内尾段。',
 '登记后，查询路径才能按前缀找到缓存。登记的是已计算状态与查找键的对应关系，不是计算注意力；基础类的完整块规则也不能概括所有子类的尾段处理。'),
'model-runner-execute': (
 'GPUModelRunner 是上游设备计算的组织者：把本轮计划整理成模型输入和注意力元数据，选择执行方式，再调用模型前向。它提供 Ascend V2 Runner 继承的基准流程。',
 'Worker 传入的 SchedulerOutput，以及流水线前一阶段可能传来的中间张量。',
 'Runner 准备输入批次、缓存块表和写入位置；根据批次及配置选择直接执行或图执行，然后调用 self.model。注意力元数据描述请求长度、缓存位置等信息。流水线并行将模型切成多个阶段，各阶段的返回内容并不相同。',
 '最后一阶段保存 execute_model_state，供独立的 sample_tokens 使用；前面的阶段可返回中间张量。execute_model 不等于生成文本，也不保证在函数内部直接完成采样。'),
'model-runner-load': (
 '模型装载阶段负责取得模型实例并准备权重。GPUModelRunner 选择合适的加载器，记录装载占用的设备内存，并处理可选的模型包装。',
 '模型配置、权重加载配置，以及可选的已有模型实例。',
 '先让 EPLB 控制器准备专家放置相关状态，再在 DeviceMemoryProfiler 的统计范围内选择 get_model_loader。没有可直接使用的模型时，由加载器实例化并装载模型；配置需要时再处理 LoRA（低秩适配权重）等包装。',
 '得到 self.model，供后续初始化和前向计算使用。模型架构解析由注册表提供，权重装载由加载器完成；定位“谁创建模型”还应继续查看具体 loader，不能在 Runner 层停止追踪。'),
'worker-gpu': (
 'GPU Worker 管理一个设备执行单元的生命周期：初始化设备和通信环境、准备模型与缓存，再把每轮计算交给 Model Runner。',
 'Executor 下发的配置和执行计划，以及设备编号、分布式通信配置。',
 '先初始化设备与分布式通信，再测量内存，创建 Runner；装载、缓存初始化、预热等请求分别转交相应方法。execute_model 还处理流水线阶段之间的张量传递，然后调用 model_runner.execute_model。',
 '执行结果经 Executor 返回 EngineCore。GPU Worker 与 NPUWorker 都实现 WorkerBase 的职责；NPUWorker 并不继承这里的 GPU Worker。'),
'executor-boundary': (
 'Executor 是引擎与设备 Worker 之间的执行边界。EngineCore 发出“初始化缓存、执行一轮、采样”等请求，Executor 负责按所选执行方式把这些请求送到 Worker。',
 '引擎配置、SchedulerOutput，以及具体操作所需的参数。',
 'get_class 根据 distributed_executor_backend 选择单进程、多进程、Ray 或自定义执行器。execute_model 通过 collective_rpc 派发到 Worker；RPC 指远程过程调用，但实际是否跨进程取决于执行器实现。',
 '把 Worker 的返回结果或 Future 交给 EngineCore。Executor 管理执行组织方式，Worker 管设备，Runner 管单轮模型输入和执行；这三层职责不能合并成一个“运行模型”节点。'),
'platform-interface': (
 'Platform 是 vLLM 为硬件差异提供的扩展接口。上游代码通过它询问设备名称、使用哪个注意力后端、如何编译及如何规划缓存等信息。',
 '上游各组件在配置、初始化或执行准备阶段发出的能力查询。',
 '接口提供属性、默认方法和可覆盖的方法。所谓钩子，就是上游会调用、平台可以给出不同实现的方法，例如 get_attn_backend_cls 和 get_kv_cache_config_builder_cls。一些方法返回类的路径字符串，供后续导入使用。',
 'NPUPlatform 继承并覆盖这些接口。Platform 不承接一条请求从头到尾的执行；插件注册、补丁、Worker 与 Runner 共同完成设备适配。'),
'platform-npu': (
 'NPUPlatform 把 Ascend 的设备能力和实现入口提供给 vLLM：选择 Worker、注意力后端、缓存布局规划器、编译器和通信器，并按配置应用适配。',
 'vLLM 的配置对象，以及平台接口发出的后端和能力查询。',
 '平台类声明 npu 设备类型，并用钩子返回 Ascend 实现的类路径。check_and_update_config 调用编译与 Worker/调度器配置函数；pre_register_and_update 应用进程级补丁。不同硬件和功能开关会选择不同实现。',
 '上游据这些路径创建或调用实际组件。返回一个类路径只是选择实现，不表示已经初始化设备、建立通信组或执行注意力。'),
'attn-backend-select': (
 '注意力后端选择函数根据模型的注意力类型、设备能力和上下文并行条件，返回要使用的后端类路径。后端随后提供计算实现类和元数据构造器。',
 'attn_selector_config 中的 use_mla、use_sparse、use_compress、use_pcp 等选择条件，以及硬件信息。',
 '函数先判断是否满足 FA3 或 FlashAttention 路径，再查普通、MLA、稀疏或压缩注意力映射；兼容硬件族和 PCP（预填充上下文并行）有各自分支。get_attn_backend_cls 还对选中的实现检查数据类型。',
 '返回用于导入后端的字符串，不创建注意力输出。MLA、SFA、DSA 是不同实现入口；名称相似不代表缓存布局或算子约束相同，须继续阅读对应后端。'),
'worker-npu': (
 'NPUWorker 管理 Ascend 设备的初始化、模型装载、缓存和执行请求。它接收 Executor 的任务，并把模型前向等工作交给 NPUModelRunner。',
 'vLLM/Ascend 配置、设备与通信信息，以及每轮 SchedulerOutput。',
 '构造时应用 Worker 进程需要的补丁、注册算子并初始化 Ascend 配置；init_device 初始化设备与通信环境，再选择 Runner。普通模型路径创建 NPUModelRunner，多模态编码器专用场景有其他 Runner。',
 'Worker 调用 Runner.execute_model 并返回结果；缓存初始化、图捕获也有独立入口。NPUWorker 与 GPU Worker 共用 WorkerBase 接口，不能把它描述成 GPU Worker 的子类。'),
'hccl-parallel': (
 '分布式初始化为多张 Ascend 卡建立通信环境和通信组。HCCL 是设备间通信后端；通信组规定一次集合通信由哪些设备参与。',
 '设备的 rank（组内编号）、world size（参与规模）、初始化地址和各类并行配置。',
 'NPUWorker 使用 hccl 初始化环境，建立上游模型并行组，再调用 init_ascend_model_parallel 添加 Ascend 需要的组。TP 把同层张量计算分到多卡；PP 把模型分阶段；PCP/DCP 分别面向预填充/解码上下文并行。',
 'MoE 通信和 EPLB 等组件随后取得相应组开展集合通信。NPUPlatform 返回通信器类路径与这里建立通信组是相关但不同的动作；具体分组与算法要继续阅读通信实现。'),
'aclgraph-integration': (
 'ACLGraphWrapper 把一次设备计算记录成可重复执行的图，以便后续相同条件的批次回放。是否直接执行、首次捕获或复用已有图，由运行模式和批次描述决定。',
 '被包装的计算函数、输入张量，以及前向上下文中的图模式与 batch_descriptor（用于区分批次形状等条件的描述）。',
 '模式不匹配时直接调用原函数。模式匹配且该批次尚无图时，使用 torch.npu.graph 捕获计算并保存图与输出引用；已有图时执行 replay。部分配置支持更新图参数后回放，或回放后更新。',
 '返回对应计算结果；输入缓冲区和地址稳定性由调用侧配合保证。编译器负责准备计算表示，图包装器负责捕获/回放，两者不是同一个阶段。'),
'compile-backend': (
 'AscendCompiler 把 PyTorch 提供的计算图交给 Ascend 编译路径，返回可执行结果与缓存句柄。编译与随后捕获、回放 NPU 图是不同职责。',
 'FX GraphModule（PyTorch 的图表示）、示例输入、编译配置和编译范围。',
 '先复制图，避免下游原地修改影响调用者；再调整示例输入的 FakeTensor 上下文。FakeTensor 是供编译分析使用的张量表示。enable_npugraph_ex 打开时调用 npugraph_ex_compile，否则调用 fusion_pass_compile。',
 '提供编译结果及相关缓存信息。compute_hash 将 torch_npu 版本等因素计入编译器 hash，但该 hash 只是完整缓存机制的一部分，不等于全部缓存键。'),
'ascend-scheduler-cls': (
 'Ascend 的调度器配置函数按功能开关修改 scheduler_cls，使引擎在创建调度器时选到相应实现。它发生在配置阶段，不是在每轮调度时临时更换对象。',
 'Ascend 的负载均衡、基于性能数据的分块、批任务调度等配置，以及是否采用异步调度。',
 '_setup_worker_and_scheduler 按分支写入 scheduler_config.scheduler_cls，必要时应用对应补丁或调整不兼容的功能。随后 EngineCore 通过 get_scheduler_cls 解析这个配置并实例化调度器。多个分支的覆盖次序应以源码为准。',
 '得到后续实际使用的 Scheduler 实现。只有相关条件成立才替换；不能推断所有 Ascend 请求都使用同一个自定义调度器。'),
'kv-config-builder': (
 'AscendKVCacheConfigBuilder 根据模型各层的 KV 缓存需求，规划缓存如何分组，以及一个全局缓存块编号需要占用多少字节。',
 'vLLM 配置与每层 KVCacheSpec；规格描述缓存布局、页大小等需求。',
 'NPUPlatform 返回此类路径。builder 在 get_kv_cache_groups 中识别特定模型及混合页大小布局；适用时采用 Ascend 分组，否则调用上游默认实现。_get_pool_bytes_per_block 对相应布局计算容量换算关系。',
 '规划结果供 EngineCore 的缓存配置流程使用，再由 Worker 准备设备缓存。builder 处理启动期布局；运行时每个请求领取与归还块由 KVCacheManager/BlockPool 处理。'),
'patch-layer': (
 '补丁层在运行时替换或补充上游对象的行为，用于尚不能单靠平台接口完成的 Ascend 适配。猴子补丁指运行时改写对象，并非修改磁盘上的原仓文件。',
 '已导入的 vLLM 模块、配置对象，以及当前处于平台初始化还是 Worker 初始化的阶段。',
 '平台期使用 adapt_patch(is_global_patch=True)；Worker 构造期应用设备执行需要的补丁。通用插件回调也会通过 _ensure_global_patch 触发进程级适配。patch/__init__.py 提供补丁目录和用途说明，具体修改以各补丁实现为准。',
 '后续调用可能执行替换后的行为。所谓“全局”是当前进程范围，不表示一个进程打补丁会自动修改所有其他进程；定位差异时须检查实际启动入口。'),
'model-registry': (
 '模型注册表根据配置中的架构名称，找到对应的 Python 模型类。它回答“使用哪个模型实现”，权重加载由模型加载器继续完成。',
 'Hugging Face 配置中的 architectures 名称，以及 model_impl 指定的实现选择。',
 'resolve_model_cls 依次处理原生、transformers 或 terratorch 等路径；懒加载对象在需要时才导入模型模块。能力检查可读取缓存，未命中时在子进程检查模型，避免主进程过早初始化 CUDA。',
 '返回模型类和实际选中的架构名。成功解析不代表权重已装载，也不保证该设备支持所有算子；_ModelInfo 保存能力信息，模块路径和类名则由 _LazyRegisteredModel 保存。'),
'model-impl-deepseek': (
 'deepseek_v2.py 展示固定版本中的 Deepseek、V2、V3 模型层如何组织注意力和 MoE 专家计算。它也包含索引器缓存规格；其他模型变体可能在别的文件中。',
 '模型配置、输入 token 对应的张量、位置和执行上下文；缓存相关组件另行提供已规划的存储。',
 '模型由多个 decoder layer 组成，注意力计算利用上下文，MoE（混合专家）按路由选择专家处理 token。DeepseekV2MoE 通过 FusedMoEFactory 构造专家层；共享专家是否合并由配置决定。索引器通过 get_kv_cache_spec 声明所需缓存规格。',
 '模型前向产生隐状态，后续 logits 和采样形成输出 token。模型声明缓存需求，但不直接从 BlockPool 领取请求块；Deepseek V3.2/V4 等实现不能仅凭名称归到本文件。'),
'plugin-registration': (
 '插件注册让 vLLM 能发现 Ascend 平台。安装包通过 entry_points 声明注册函数，函数返回 NPUPlatform 的完整模块与类路径，供上游后续导入。',
 '安装包元数据，以及 vLLM 对平台插件和通用插件的发现过程。',
 'setup.py 将 vllm.platform_plugins 中的 ascend 项指向 vllm_ascend:register；register() 返回 vllm_ascend.platform.NPUPlatform。另有通用插件回调，用于连接器、模型加载器等注册，并可先应用进程级补丁。',
 '平台实现成为可被上游发现的入口。register 返回路径，不在此函数中创建 Worker 或执行模型；上游逐步加载机制应继续阅读插件系统实现。'),
'ascend-model-runner': (
 'NPUModelRunner 继承上游 GPUModelRunner 的执行组织方式，并补充 Ascend 的缓存、图执行、采样和专家负载均衡处理。NPUWorker 创建并调用它。',
 'NPUWorker 传入的本轮执行计划、可能的中间张量，以及设备与模型配置。',
 '构造时准备 Ascend 状态并创建 EPLB 控制器。execute_model 在上游前向流程前后处理 Ascend 的缓存传输和执行状态；sample_tokens、load_model、capture_model 分别处理采样、装载和图捕获。图管理器包装在限定作用域内替换上游管理器，结束时恢复。',
 '返回执行结果，或保存供后续采样使用的状态。继承关系表示代码复用，不表示先在 GPU 上执行再转到 NPU；这两列是上游实现与平台适配的对照。'),
'moe-comm': (
 'MoE 通信把 token 送到持有所选专家的设备，执行专家计算，再把结果合并回来。Ascend 将多种通信方式封装为 MoECommMethod 的不同实现。',
 '待处理的 token 张量、专家路由结果、专家权重及量化参数。EP（专家并行）表示把专家分布到不同设备。',
 '普通流程是 token_dispatch 分发、专家 MLP 前馈计算、token_combine 合并。AllGather、AlltoAll、MC2、FusedMC2 的通信与融合方式不同；初始化注册可选实现，运行上下文再决定使用哪一种。FusedMC2 内还按条件选择 MegaMoe 或 dispatch_ffn_combine。',
 '返回专家计算结果及专家 token 计数，计数可供 EPLB 记录负载。注册了某个实现不等于每轮都选择它，也不表示所有硬件都支持同一种融合算子。'),
'eplb': (
 'EPLB 根据各专家承担的 token 数调整专家的设备分布，减少部分设备过忙、其他设备等待的情况。它管理专家放置和路由，不负责为每个请求分配生成 token 预算。',
 '专家负载计数、已有专家映射、并行配置与负载均衡策略。',
 'V2 路径由 AscendEPLBController 按批次阶段控制是否收集负载，AscendEplbState 汇总统计并参与重新分配。专家映射变化后，要与权重迁移及设备上的路由表更新协调；迁移通信方式取决于配置。另有 eplb/ 下的子进程和 D2D 路径，须与 V2 分开阅读。',
 '更新后的专家放置与路由供后续 MoE 执行使用。MoE 通信负责这一轮 token 的分发与合并，EPLB 负责基于统计调整后续执行所用的专家布局。'),
}

MODULES = {
'entry': '职责：把用户输入变成引擎请求，再把引擎结果还给调用者。发送方向：输入处理器 → EngineCoreRequest → EngineCoreClient → 引擎。返回方向：引擎输出 → OutputProcessor → 反分词/停止检查 → 请求队列。跨进程边界由所选客户端决定；本模块不执行模型前向。',
'engine': '职责：连接调度与设备执行。启动时规划 KV 缓存并通知 Worker 初始化；运行时向 Scheduler 要本轮计划，经 Executor 派给 Worker，再把模型结果交回 Scheduler 更新。请求与结果可以跨多个进程，实际通信方式由客户端和执行器决定。子模块 step 展示一种执行入口，不覆盖所有 batch_queue 路径。',
'cache': '职责：决定这一轮算什么，并管理请求需要的 KV 缓存空间。接收引擎中的请求和状态，输出 SchedulerOutput 给引擎；请求级 KVCacheManager 经分组管理器与 BlockPool 完成前缀查询、领取、登记和回收。这里管理计算进度与缓存位置，设备上的注意力计算由执行模块完成。',
'execution': '职责：展示上游 GPU Worker、Runner 和采样流程，作为 Ascend 适配的对照。输入来自 Executor 的本轮计划；Worker 管设备，Runner 准备输入并调用模型，采样器把 logits 变成 token。结果经执行器返回引擎。与 NPU 模块的继承/适配连线表示实现关系，不表示一次请求先跑 GPU 再跑 NPU。',
'model': '职责：说明模型实现如何被找到、装载并执行。注册表把架构名称解析为类；Runner 经加载器取得模型和权重，再在每轮前向调用模型。模型中的注意力和专家层使用执行侧提供的缓存与通信能力；模型声明 KV 需求，缓存规划和请求块分配由其他模块完成。',
'interface': '职责：定义上游可向设备适配层询问的能力和实现入口，例如注意力后端、编译器、通信器和缓存规划器。Ascend 的 NPUPlatform 继承并覆盖这些方法。这里是接口与默认实现，不是请求主线中的一个必经执行阶段。',
'platform': '职责：让上游发现 Ascend，并选择匹配的实现。插件注册暴露 NPUPlatform；平台根据配置提供 Worker、注意力、编译、通信及缓存规划入口，并应用所需补丁。主要作用在发现、配置与初始化阶段；返回类路径不代表已经执行相应计算。',
'npu': '职责：实际组织 Ascend 设备上的模型执行。Executor 把计划交给 NPUWorker，Worker 再调用 NPUModelRunner；Runner 使用注意力后端、图执行和并行通信能力，并通过模型完成前向与采样。结果经 Worker/Executor 回到引擎。上游 GPU 模块提供被复用的代码，不是 NPU 路径之前的计算阶段。',
'attention': '职责：把模型所需的注意力计算落实到 Ascend 后端。平台先选后端类；后端提供实现类和元数据构造器。执行侧准备 Q/K/V 或 MLA 隐状态、缓存和请求长度，元数据描述缓存位置，注意力实现计算并写入输出。普通注意力、MLA 和 310P 是不同条件下的路径，不是依次执行的步骤。',
'compile': '职责：准备和复用设备计算。AscendCompiler 接收上游计算图并选择编译路径；图包装器在适用模式下捕获计算，后续按匹配的批次描述回放。Runner/Worker 的初始化、预热与前向会使用这些能力。编译、首次捕获、后续回放应分别理解，不能合成每轮必经的单一路径。',
'parallel': '职责：协调多设备和专家计算。Worker 初始化通信环境与组；MoE 通信在每轮把 token 分发到专家并合并结果；EPLB 根据累计负载调整专家布局和路由。三者分别回答“谁参与通信”“本轮数据怎么走”“后续专家放在哪里”，并不是一个函数的三个别名。',
'engine-step-cycle': '阅读 EngineCore.step 的一次迭代：检查请求 → 调度 → 记录执行计数 → 经 Executor 派发 → 准备约束并等待结果 → 必要时采样 → 处理中止 → 更新请求状态 → 返回。子模块是同一个函数的代码片段；模块层级表达学习组织，不代表新增进程或组件。',
}

# One explicit explanation per existing edge. Labels remain stable because the
# edit protocol identifies an edge by (From, To, Label); Evidence is shown in details.
RELATIONS = [
'异步消息边界：AsyncLLM 经 EngineCoreClient 提交请求；EngineCore 的循环稍后选择执行入口。此边不是 AsyncLLM 直接调用 step，仅描述多进程客户端的路径。',
'任务启动：AsyncLLM 首次接收请求时创建输出处理协程；后续请求复用同一后台任务。',
'读取依赖：箭头从输出协程指向它读取的引擎；结果数据实际从 EngineCore 经客户端流向输出协程，箭头不是结果传输方向。',
'直接调用：EngineCore.step 调用 Scheduler.schedule，传入是否节流预填充的条件，取得本轮 SchedulerOutput。',
'持有与派发：EngineCore 创建 Executor 实例，每轮把 SchedulerOutput 交给它执行，并在需要时请求采样。Executor 再驱动 Worker。',
'启动期数据依赖：EngineCore 先生成 KVCacheConfig，再用该配置构造 Scheduler；不是初始化函数每轮调用 schedule。',
'配置选择：缓存规划流程通过平台钩子选用 AscendKVCacheConfigBuilder，得到布局和容量计算规则；此边不表示初始化函数直接写设备缓存。',
'逐层转交：EngineCore 把各 Worker 的缓存配置交给 Executor，Executor 调用 NPUWorker.initialize_from_config；是否跨进程由执行器决定。',
'条件调用：调度器申请缓存空间失败时，可能选择一个请求并调用 _preempt_request，让其暂时退出运行。',
'分层分配：Scheduler 调用请求级 KVCacheManager.allocate_slots，再由中间协调器和分组管理器处理具体块；Scheduler 不直接调用图中块级分配器。',
'同组件的另一方法：这一关系属于 Scheduler.update_from_output 确认进度后的缓存登记，不应理解成 schedule 必定直接调用 cache_blocks。',
'生命周期关系：抢占通过 KVCacheManager 释放该请求拥有的块，最终归还 BlockPool；分配节点用于说明被释放的块来源，不表示调用 allocate_new_blocks 释放块。',
'配置依赖：箭头从块分配器指向它依赖的启动期配置；配置实际从初始化流程经 Scheduler/KV 管理器传入分配体系。',
'执行派发：Executor 经 collective_rpc 请求 Worker.execute_model，传入本轮计划。RPC 是否跨进程由具体执行器决定。',
'直接调用：GPU Worker 将本轮计划及流水线中间张量交给 GPUModelRunner.execute_model。',
'装载委托：GPU Worker 创建 Runner，并在装载阶段调用 Runner.load_model；这是启动/装载路径，不是每轮前向都重装权重。',
'间接能力查询：Runner 初始化注意力实现时经 init_attn_backend 查询当前平台；Platform 提供后端选择接口，不计算注意力。',
'间接解析依赖：Runner 选择加载器，加载器使用注册表解析出的模型类进行装载；不是注册表直接读取全部权重。',
'类继承：NPUPlatform 是 Platform 的子类，覆盖设备接口。此边表示类型和接口实现关系，不传递生成请求。',
'注册数据：Ascend 插件的 register 返回 NPUPlatform 的完整类路径，供上游后续导入；返回路径不等于创建平台执行任务。',
'初始化调用：通用插件的注册回调先确保当前进程的全局补丁已应用，再进行相应注册。',
'配置选择：NPUPlatform 按硬件和开关写入 worker_cls；执行器之后按配置创建 Worker。并非所有硬件分支都选择同一个 Worker 类。',
'类路径选择：NPUPlatform 的缓存规划钩子返回 AscendKVCacheConfigBuilder；实际调用规划方法发生在缓存配置流程。',
'能力与类路径：NPUPlatform 声明支持静态图，并返回 ACLGraphWrapper 路径；此处不捕获或回放图。',
'类路径选择：NPUPlatform 提供 AscendCompiler 和图优化 PassManager 的入口，编译工作由后续编译流程触发。',
'条件配置修改：平台配置函数按功能开关修改 scheduler_cls；EngineCore 随后按此字段构造调度器。',
'直接调用：平台的 pre_register_and_update 触发 adapt_patch(is_global_patch=True)，修改当前进程的适配行为。',
'通信接口选择：平台返回 NPUCommunicator 类路径；Worker 初始化通信组是另外一个步骤，不能把返回路径等同于建立 HCCL 组。',
'代码归属：后端选择函数是 NPUPlatform 中的方法；箭头表示它属于该平台类，不是选择函数创建平台。',
'接口覆盖：Ascend 的后端选择实现覆盖上游 Platform 的默认钩子；这不是两个注意力实现依次执行。',
'被选择的分支：兼容硬件族的选择表可返回 310P 后端；箭头从具体实现指向选择逻辑，实际选择方向由选择器到实现。',
'替换关系：符合开关条件时，Ascend 配置使引擎选择专用 Scheduler 实现；不是每轮先执行通用调度器再执行一遍 Ascend 调度器。',
'配置归属：选择专用调度器的代码属于平台配置流程；该节点没有独立的设备执行生命周期。',
'接口实现：Ascend builder 继承上游默认布局规划器，由 Platform 的 builder 选择钩子暴露；它不实现整套 Platform 类。',
'同接口的不同实现：NPUWorker 与 GPU Worker 都继承 WorkerBase。此边是平台实现对照，不是 NPUWorker 继承 GPU Worker，也不是请求先经过 GPU 再经过 NPU。',
'创建与调用：NPUWorker 在普通模型路径创建 NPUModelRunner，并在每轮把 SchedulerOutput 交给其 execute_model。',
'初始化调用：NPUWorker 初始化设备时调用分布式环境初始化，建立通信后端和所需通信组。',
'进程内初始化：NPUWorker 构造时应用该 Worker 进程需要的补丁；其他进程是否应用补丁由它们各自的入口决定。',
'间接触发捕获：非 enforce_eager 等适用条件下，Worker 预热阶段调用 Runner.capture_model；图管理器/包装器共同完成捕获，不是 Worker 直接执行所有包装器逻辑。',
'类继承与复用：NPUModelRunner 继承 GPUModelRunner，在覆写的方法中可调用 super。GPU 是上游类名，不表示 NPU 路径先在 GPU 硬件上计算。',
'图管理适配：NPU Runner 在限定上下文内把上游图管理器工厂替换为 ModelAclGraphManager；ACLGraphWrapper 是图执行体系中的包装器，不能把管理器和包装器当成同一个类。',
'持有与驱动：NPU Runner 构造 AscendEPLBController，并把批次阶段交给它，以控制是否采集专家负载。',
'配置适配：补丁调整 V2 Runner 选择及功能检查；Worker 之后按配置创建 Runner，不是补丁函数执行模型前向。',
'注册数据依赖：架构注册表为 resolve_model_cls 提供模型描述和懒加载对象，解析器据此找到实现类。',
'实现选择：当架构名映射到 deepseek_v2 文件中的对应类时，注册表返回该类；其他 Deepseek 变体可能指向其他文件。',
'需求数据：模型中的缓存组件声明 KVCacheSpec，EngineCore 经 Worker/Executor 汇总这些规格，再规划缓存；声明规格不是领取请求缓存块。',
'间接实现路径：Deepseek 专家层经 FusedMoEFactory 构造专家计算，Ascend 适配路径可使用相应 MoE 通信实现；选择中间层不在此边中展开，不能声称两端直接互调。',
'统计数据契约：MoE 执行结果包含专家 token 计数，EPLB 记录这些计数用于后续负载均衡；此边不代表 MoE 每次执行都会立即重排专家。',
'通信组依赖：MoE 通信实现取得 MC2 等通信组，用于专家数据交换；组由初始化流程建立，专家计算本身不重新创建全套分布式环境。',
'集合通信依赖：EPLB 使用专家相关通信组汇总负载与协调状态。具体操作包括对计数求和；它不负责调度请求的 token 预算。',
'请求对象使用：AsyncLLM 接受或构造 EngineCoreRequest，再把它登记到输出处理器并提交给客户端；对象不是可执行任务函数。',
'匹配数据：request_id 让输出处理器把引擎结果匹配回对应请求队列；数据对象本身不会启动或调用输出协程。',
'阅读层级：空请求检查是 EngineCore.step 内的第一段代码；子节点用于展开阅读，并非独立函数调用。',
'条件控制流：仅当调度器仍有请求或待清理状态时继续调度；否则前一段已经返回。',
'同函数内顺序：取得 SchedulerOutput 后，读取其总 token 数决定是否增加计数。',
'同函数内顺序：派发计算得到 Future 后准备语法约束；计算可能仍在设备侧执行，不能把箭头理解成计算已经完成。',
'同函数内顺序：准备约束后进入统计/错误上下文，等待 Future；只有结果为 None 才另行采样。',
'同函数内顺序：执行或采样结果取得后，先处理执行期间到达的中止请求。',
'同函数内顺序：中止处理完成后，调用 Scheduler.update_from_output 更新状态。',
'同函数内顺序：取得更新后的输出包，再附加迭代统计和权重版本。',
'同函数内顺序：输出元数据补充后，返回输出字典和非零 token 标志。',
'同函数内顺序：计数只在 token 数非零时增加，但 Executor.execute_model 不在这个 if 分支内；存在待清理状态而本轮零 token 时仍可能派发。',
'直接调用：AsyncLLM 调用输入处理器，把提示词和参数转换成引擎请求；原始输入和已渲染输入走不同分支。',
'创建数据对象：InputProcessor.process_inputs 校验并整理字段后返回 EngineCoreRequest。',
'客户端依赖：多进程配置下，AsyncLLM 经客户端 add_request_async 提交请求；不是直接调用 EngineCore.step。',
'编码的数据类型：AsyncMPClient 编码 EngineCoreRequest 并附带请求类型；箭头指向消息载荷定义，不是向请求对象发起执行调用。',
'数据转换：引擎把 EngineCoreRequest 的字段交给 Request.from_engine_core_request，建立调度使用的内部状态对象。',
'类型与学习关系：RequestStatus 定义 Request 可使用的状态值；枚举声明顺序不是请求必经顺序。',
'函数返回值：Scheduler.schedule 返回 SchedulerOutput，描述这一轮允许执行的请求与 token 数。',
'输入数据流：执行计划经 EngineCore、Executor、Worker 传给 Runner；SchedulerOutput 是数据对象，不主动调用模型。',
'概念配对：缓存登记建立前缀到已计算缓存的对应关系，缓存查询读取这个对应关系；该边是学习对照，不表示查询函数调用登记函数。',
'直接调用：分组缓存管理器需要新块时调用 BlockPool.get_new_blocks；后者从空闲队列领取并标记引用。',
'逐层回收：抢占请求的块经过 KV 管理器或延迟释放队列归还 BlockPool。设备仍可能写入的块要等待安全时机，不能立刻复用。',
'生命周期对照：领取和归还是块池的两个操作；归还到零引用时才可再领取。此边不表示 get_new_blocks 直接调用 free_blocks。',
'可选路径对照：普通采样与草稿验证都产生采样结果；草稿存在且配置适用时走验证，不是每次普通采样后都追加验证。',
'直接调用：输出协程把收到的一组 EngineCoreOutput 交给 OutputProcessor.process_outputs，后者按请求编号分发结果。',
'直接委托：OutputProcessor 调用请求的 detokenizer.update，把 token 转成文本并检查停止条件；检测到停止后可能通知引擎中止。',
'准备与使用阶段：语法初始化生成可用的约束对象，后续调度步骤取得位掩码供采样使用；grammar_init 不直接调用 step。',
'选择类路径：平台选择器在普通注意力条件下返回 AscendAttentionBackend；其他条件可能返回 MLA、稀疏或兼容后端。',
'返回类型：后端的 get_builder_cls 返回元数据构造器类；DCP 条件下返回专用类，不是立刻构造本轮元数据。',
'返回类型：后端的 get_impl_cls 返回计算实现类；DCP 条件下另选专用实现，不是调用 forward。',
'元数据契约：构造器生成缓存块位置、请求长度等 AscendMetadata，执行路径再把它传给 forward；构造器不直接运行注意力。',
'间接选择：平台返回 MLA 后端类，该后端再提供 MLA 实现；模型满足相应条件时才进入这条前向路径。',
'方法细分：sample_tokens 是 NPUModelRunner 的独立方法；此边用于从类总览进入采样阅读，不表示 execute_model 无条件调用它。',
'间接代码复用：NPU sample_tokens 调用上游采样入口；实际采样分流在 Runner 的其他方法中，普通路径最终使用 Sampler。',
'条件执行路径：输入批次带草稿且拒绝采样器可用时，Runner 进入草稿验证；不同支持版本有不同参数契约，两仓快照不是已验证的兼容组合。',
'逐层配置数据流：EngineCore → Executor → NPUWorker.initialize_from_config → Runner.initialize_kv_cache；这些层是否同进程取决于执行器拓扑。',
'学习前置：先理解请求如何取得块，再理解已计算块如何登记到前缀缓存。CoW 检查已有部分命中标记，不是这一段代码创建该命中标记。',
]


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--snapshot', type=Path, required=True)
    ap.add_argument('--out', type=Path, required=True)
    args = ap.parse_args()
    graph = next(g for g in json.loads(args.snapshot.read_text(encoding='utf-8-sig'))['Topics'] if g['Id']=='vllm-ascend-source')
    # Freeze the reviewed base, not just its edge count: this indexed table must
    # never attach explanations to a newly reordered or edited graph.
    assert graph['Revision']=='54a45646557405c8ffaa2aa90f6a629ffb877971a2cc595a58ff11a384b296f7', 'Base changed; re-review before generating.'
    assert len(graph['Edges']) == len(RELATIONS) == 88
    nodes = []
    for n in graph['Nodes']:
        if n['Id'] in EXPLANATIONS:
            role, inputs, steps, outputs = EXPLANATIONS[n['Id']]
            nodes.append(dict(Id=n['Id'], Summary=role, Approach=f'输入：{inputs}\n\n处理过程：{steps}\n\n输出与衔接：{outputs}\n\n源码定位：文件、符号与固定提交链接见代码和资料页；本说明基于静态源码复核。'))
    # These already-readable nodes need only short definitions or a precise actor.
    extra = {
      'engine-core-request-struct': '请求对象使用 msgspec.Struct 定义：这是便于结构化编码和解码的数据类型，不是调度器，也不是 dataclass。输入 token/嵌入、采样参数和请求编号随对象交给引擎。',
      'engine-step-detail-grammar': 'EngineCore.step 在等待模型结果前，向 Scheduler 获取语法位掩码。位掩码是一组用于标记哪些 token 符合当前语法约束的数据，随后交给需要约束的采样路径。',
      'engine-step-detail-wait-sample': 'EngineCore.step 在记录迭代统计和错误信息的上下文中等待执行结果；执行结果为 None 时，才调用 Executor.sample_tokens 继续取得采样输出。这里的 None 是该执行接口的分支约定，不表示请求没有任何生成结果。',
      'engine-step-detail-update': 'EngineCore.step 将本轮计划和模型输出交给 Scheduler.update_from_output。Scheduler 更新内部请求状态，并返回按 client_index（前端客户端编号）分组的 EngineCoreOutputs；每包可含多个请求的结果。',
      'input-process': '输入处理器接收 EngineInput（已渲染的引擎输入）或旧式原始提示词与参数，检查任务、LoRA 低秩适配权重、设备和输入限制，返回 EngineCoreRequest。它处理输入，不执行模型前向。',
      'request-runtime': '引擎把前端传来的 EngineCoreRequest 转成内部 Request。内部对象记录请求在调度中的状态；转换方法同时接收用于生成缓存查找摘要的 block_hasher，以及可选的已生成块摘要。',
      'ascend-attn-forward': '普通 Ascend 注意力实现接收 Q（查询）、K（键）、V（值）、历史 KV 缓存、请求元数据和输出缓冲区。它按需写入新的 K/V，再计算当前 token 的注意力结果并写回输出。',
      'ascend-mla-forward': 'MLA（多头潜在注意力）用压缩表示组织 KV 缓存。AscendMLAImpl.forward 接收 hidden_states（模型隐状态）、缓存和请求元数据，分别处理预填充与解码部分，再进行输出投影。',
    }
    nodes += [dict(Id=k,Summary=v) for k,v in extra.items()]
    oldnodes = {n['Id']:n for n in graph['Nodes']}
    assert len({n['Id'] for n in nodes}) == len(nodes)
    assert all(n['Id'] in oldnodes for n in nodes)
    modules = []
    step_ids = ['empty','schedule','count','dispatch','grammar','wait-sample','aborts','update','attach','return']
    overrides = {n['Id']:n for n in nodes}
    for m in graph['Architecture']['Modules']:
        summary = MODULES.get(m['Id'])
        if m['Id'].startswith('engine-step-') and m['Id'] != 'engine-step-cycle':
            idx = int(m['Id'].split('-')[2])
            id = 'engine-step-detail-'+step_ids[idx]
            summary = overrides.get(id,oldnodes[id])['Summary']+' 这是 step 内部的阅读片段，不是独立执行组件。'
        assert summary, m['Id']
        modules.append(dict(Id=m['Id'],Summary=summary))
    edges = copy.deepcopy(graph['Edges'])
    for e, explanation in zip(edges,RELATIONS):
        e['Evidence'] = explanation+'\n\n源码依据与范围：'+e['Evidence']
    args.out.mkdir(parents=True,exist_ok=True)
    delta = dict(TopicId=graph['Id'],ExpectedRevision=graph['Revision'],Nodes=nodes,Edges=edges,Modules=modules,NodeModules=[])
    (args.out/'graph-delta.json').write_text(json.dumps(delta,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    report = dict(ReviewedNodes=len(graph['Nodes']),RewordedNodes=len(nodes),ClarifiedRelations=len(edges),ClarifiedModules=len(modules),
                  Principles=['明确动作执行者、输入、输出和条件。','区分运行期调用、配置选择、数据契约、类型继承和阅读关系。','解释跨模块箭头何时表示依赖而非数据方向。','保留源码、版本、代码、个人复述与已有层级；原文修改后旧术语翻译可被应用标为过期。'],
                  ChangedNodeIds=[n['Id'] for n in nodes],NoNewRuntimeClaims=True)
    (args.out/'review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps(report,ensure_ascii=False))


if __name__=='__main__':main()
