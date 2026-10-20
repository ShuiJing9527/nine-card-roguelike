# BattleDemo：在 Unity 编辑界面与替换表现

Unity 6000.0.66f2，保留原 URP 3D 项目。项目根目录为 `nine-card-roguelike/`；以下 Assets 路径均相对于此目录。

## 打开与编辑

打开 `Assets/Scenes/BattleDemo.unity`。**停止 Play 后再修改和保存**。Hierarchy 中展开：

- `Qiling Battle Demo / Battle UI / Selection Page`：武器、遭遇、种子选择。
- 同级 `Combat Page`：战斗完整布局，默认处于 inactive。临时启用它、停用 Selection Page，即可在 Scene/Game 中编辑；Play 会自动选择正确页面。
- `Replaceable 2.5D Stage`：世界相机、灯光、背景和三个 Spawn 位置。可直接修改位置并保存场景。

主页面是场景内的 Prefab 实例；可以直接修改 RectTransform、Image、Text、Button 等组件，保存场景保留局部覆盖。希望复用到其他场景时，再把对应修改 Apply 到 Prefab。层级中添加装饰对象不会影响绑定。保留原有绑定组件、主要节点和相对父子关系，**不要复制 BattleUiElement 的绑定标识或删除有绑定的节点**；脚本丢失绑定时会给出警告并使用配置中的基础元素占位。

## 资产位置

| 资产 | 路径 | 用途 |
| --- | --- | --- |
| 界面与共用表现配置 | `Assets/Presentation/BattleViewConfig.asset` | 主页面/弹窗、字体、卡牌交互、公共图标、颜色、占位资源、飘字 |
| 主界面 | `Assets/Presentation/UI/SelectionPage.prefab`、`BattlePage.prefab` | 非 Play 模式下双击进入 Prefab Mode 编辑 |
| 弹窗 | 同目录 `SettingsOverlay.prefab`、`PreviewOverlay.prefab`、`LogOverlay.prefab`、`TraitTestOverlay.prefab` | 设置、预览、日志、重铸，运行时从这些 Prefab 实例化 |
| 复用 UI | `Assets/Presentation/UI/Components/` | Card、CardHitRegion、SequenceSlot、Button、HealthBar、UnitHud、EnemyIntent、ActionAvatar、StatusIcon、FloatingText、Element |
| 单位与背景 | `Assets/Presentation/Units/` | Player、Slime、RainbowBoss、Lava、Poison 占位 Prefab 和 Background |
| 共用攻击表现 | `Assets/Presentation/Skills/DefaultAttack.asset` | 动作、曲线、前后摇、位移、飞行物、命中特效、音效、震动 |
| 默认效果与材质 | `Assets/Presentation/Effects/`、`Materials/` | 可替换的 Projectile、HitEffect 及占位材质 |
| 卡牌、技能、武器、敌人 | `Assets/Data/BattleCatalog.asset` | 规则数据与各项资源引用，原公式与数值未变 |

重复 UI 使用嵌套 Prefab，并保存各实例的文字、尺寸等差异。修改共用组件时注意实例上已经存在的 Overrides；需要继承新值时，在 Unity 的 Overrides 中有针对性地 Revert 对应字段。不要一键 Revert 整个页面，否则会丢掉各个实例的布局和绑定差异。

## 替换一个敌人

1. 复制 `Units/SlimePlaceholder.prefab` 到自己的目录，双击进入 Prefab Mode。
2. 保留根节点 `UnitPresentation` 和 `Visual Root`。把 `Replace Model Or Sprite Here` 替换为模型，或带 SpriteRenderer 的对象；修改材质、Sprite、朝向和缩放。
3. 在 UnitPresentation 中关联 Visual Root。需要直接切换 Sprite 时关联 Sprite Renderer 和 Sprite；可配置 Material Override、Animator、Animator Controller、Motion、Visual Scale 和待机浮动幅度/周期。
4. 移动子挂点 Health Anchor、Launch Anchor、Hit Anchor、Ground Anchor；Status Anchor 可按需要关联，未关联时状态图标跟随血条。
5. 在 `BattleCatalog.asset / Monsters / 对应敌人 / Visual Prefab` 指向新 Prefab；Portrait 指定行动轴头像。
6. 保存资产，重新 Play 对应遭遇。Prefab 的外观、缩放和挂点不会修改敌人 HP、双防、段数或技能。

模型的生成位置来自 Scene 中 Enemy Spawn；已有 Prefab 的根缩放不会再被“Boss/普通怪”的硬编码尺寸覆盖。若替换成 2D 素材，可在 SpriteRenderer 对象上直接调整面向相机的角度。

## 替换一张卡的外观

1. 在 `BattleCatalog.asset / Cards / 对应卡` 中设置 **Frame、Art、Type Icon**；不设置时分别沿用卡面 Prefab/公共底框、Skills 的 Icon/文字占位，以及公共类型图标。
2. 只想换技能图片时，在 `Skills / 对应技能 / Icon` 设置图片。词条图标可以在 `Traits / Icon` 或 ViewConfig 的 Traits 中设置，卡片上的 1～3 个图标随当前重铸配置刷新。
3. 希望调整卡面整体：打开 `UI/Components/Card.prefab`，或在 BattlePage 的 Hand Faces 下修改某一张卡的 Image、Text、边框、字号和 RectTransform。所有卡名、类型和词条内容仍由实际数据绑定。
4. 卡面位置和旋转保存在 Hand Faces 的九个实例上；点击层位于 Hand Hit Regions，初始化时会跟随卡面位置。悬停抬起/缩放由 ViewConfig 控制；点击分区的 Band Min/Max 可在对应 CardHitRegion 的 HandCardInteraction 上调整。
5. 改共用字体用 ViewConfig.Font；某个 Text 要独立字体，可取消同对象 BattleUiElement 的 Use Theme Font。默认字体资产使用系统字体；跨电脑时可改成项目内导入的 .ttf/.otf 字体。

## 替换攻击动作、飞行物、命中特效

1. 在 Project 中 Create → 器灵战斗 → 技能表现，或复制 DefaultAttack.asset。
2. 为技能单独指定：`BattleCatalog / Skills / 对应技能 / Presentation`。未指定时使用施法者 UnitPresentation.Motion，再回退到 ViewConfig.Default Skill。
3. 动作可通过 Animator Controller 的状态名（Idle/Attack/Hit/Death 可改）播放，也可直接指定 Attack Clip、Hit Clip、Death Clip。直接 Clip 用 Playables 播放；不用添加动画事件。受击和死亡优先读取被击中单位自身的 Motion，便于给每个敌人单独配置；Animator Transition、Clip Speed 位于 UnitPresentation。
4. Projectile 接收飞行物 Prefab；Impact Effect 接收命中特效 Prefab。Cast Effect、End Effect 分别在施法开始、结束生成。Launch Anchor 是发射位置，Hit Anchor 是命中位置，Ground Anchor 是结束地面效果位置。
5. 编辑 Windup、Travel、Hit Duration、Recovery、Death Duration、Effect Lifetime，Move/Projectile/Shake 曲线、前冲/受击距离、受击缩放/颜色、抛物线高度、资源缩放/旋转、Shake 幅度/时长和 Cast/Launch/Impact/End 音效及音量。
6. 保存，再 Play。每个原生段、追击、反击、弹射都读取结算器记录的实际目标；死亡转火后，后续飞行物会飞向新目标。闪避/格挡显示对应提示，不播放命中受击效果。

表现节点是 **CastStart → 每段 Launch → Impact → CastEnd**，必要时插入 Death；状态/间接伤害提供 Impact/Death。表现先读取已经完成的逻辑结果，再依次播放，不重新掷骰，也不读取新目标来替换实际结算目标。每段的实际事件可从 BattleEngine.VisualEvents 与 BattleDemo.PresentationTrace 对照。

动画/特效缺失时仍可继续：缺失 Animator 状态会跳过；没有动画时用可编辑的小幅位移/缩放；没有飞行物或特效就跳过该资源；预留等待由时长参数控制，单项限制在 0～5 秒。战斗不会等待动画事件或特效回调。重新挑战会取消旧表现队列并恢复单位。

## 已外置与仍需代码的边界

已可在 Inspector/Prefab/Scene 编辑：主要页面/弹窗布局、字体/字号/颜色、按钮与面板图像、血条样式、卡面及图标、状态与护盾图标、头像、世界相机/灯光/背景、Spawn 位置、模型/Sprite/材质、挂点、Animator/Clip、特效/飞行物/音效，以及上述动作时长、曲线、距离、缩放和震动参数。静态 Text/Image 的颜色与尺寸在对应 Prefab 内修改；ViewConfig 的文字颜色是迁移/缺失元素的默认值，运行时选择描边、卡牌选中、头像及图标颜色各有对应配置，不会强制覆盖你给已有文字设置的颜色。

仍需改代码：增加新规则/技能效果类型、改变九卡五槽的结构数量、改变动态数据绑定逻辑或表现节点语义、加入新的交互方式或复杂动画编排。当前保留一次性迁移构造器和缺失组件时的占位构造路径；正常启动优先使用已保存对象，不用这些默认坐标覆盖编辑器布局。

`器灵战斗/外置界面与表现` 菜单是首次迁移入口；已有配置时直接退出，不覆盖你的 Scene/Prefab 修改。后续日常修改不需要重跑迁移，也不要用“从规则快照重建数据库”来修改素材。

原自由重铸、配置保存、同种子/换种子重试和 JSON/TXT 导出继续保留。导出入口仍在设置和日志中，目录为 `Application.persistentDataPath/QilingDebug/Exports/`。
