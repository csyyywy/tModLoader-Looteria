# Looteria · 掠夺之地

把暗黑破坏神3 / 流放之路 / 我的世界地下城的刷宝机制带进泰拉瑞亚的**纯系统模组**。

- **零新增内容**：不新增任何物品/怪物/方块/增益；全部机制挂在 Global 钩子上，对**原版与所有其它模组的物品**生效。
- 环境：Terraria 1.4.4 + tModLoader 1.4.4 stable + .NET 8.0.423。

## 玩法

1. **稀有度**：普通 / 魔法 / 稀有 / 传说 / 套装，掉落时随机。
2. **词缀**：武器/护甲/饰品数值词缀（伤害/暴击/攻速/防御/生命/移速/状态免疫/挖掘/渔力…），随物品 tier 缩放，同属性反饱和防爆炸。
3. **力量等级（gear score）**：装备总评，用于秘境门槛；**血量前方常驻显示**（占位图标，可替换 `Content/UI/PowerIcon.png`）。
4. **宝石插槽**：传说/套装带 1~2 槽，6 种宝石 × 4 级。
5. **主题套装**：同主题装备 ≥2/4/6 件激活套装加成。
6. **传说之力**：10 条传奇特效（连锁闪电/灼烧/嗜血/处刑/穿透/分裂/天雷/荆棘/顺劈/狂乱）。
7. **秘境**：D3 式冲层，敌人缩放，逐层解锁，通关发奖。
8. **经济**：血岩（击杀/秘境）抽奖；重铸之尘（拆解/秘境）重铸词缀/升档。

## 操作

| 按键/命令 | 说明 |
|---|---|
| `P` | 打开/关闭掠夺面板（可改键） |
| `/loot info` | 查看手持物品词缀详情（聊天可用） |
| `/loot tier` | 查看手持物品 tier/类别/资格（聊天可用） |
| `/loot riftinfo` | 秘境调试信息（完整内容写日志，搜 `RiftInfo:`；聊天只给摘要） |
| `/lootadmin roll <rarity>` | 给手持物品掷稀有度（none/magic/rare/legendary/set 或 0-4；单机聊天 / 服务器控制台） |
| `/lootadmin clear` | 清除手持物品词缀并还原售价（单机聊天 / 服务器控制台） |
| `/lootadmin salvage` | 拆解手持词缀物品 → 重铸之尘（单机聊天 / 服务器控制台） |
| `/lootadmin shards <n>` `/lootadmin dust <n>` | 设置货币（单机聊天 / 服务器控制台） |
| `/spike set <n>` `/spike info` | 开发期持久化验证命令（单机聊天 / 服务器控制台） |

> 多人安全（H3）：写操作命令全部收归 `/lootadmin`（`CommandType.Chat | CommandType.Console`）——聊天命令
> 在多人里任何玩家都能输入 `/loot shards 99999` 作弊，故写操作对**多人聊天一律拒绝**，只允许
> 单机聊天与服务器控制台；只读查询保留 `/loot`。
> 货币改动经 `Looteria.SendCurrencyTo` 定向推送（H5：下行包不再被 whoAmI 哨兵守卫丢弃）。

## 跨模组 Mod.Call API

```csharp
Mod loot = ModLoader.GetMod("Looteria");
if (loot != null) {
    bool elig   = (bool)loot.Call("IsEligible", item);          // 物品是否能带词缀
    int rarity  = (int)loot.Call("GetRarity", item);            // 0-4
    int power   = (int)loot.Call("GetPowerScore", item);
    bool ok     = (bool)loot.Call("RollAffix", item, 3);        // 传说
    loot.Call("ClearAffix", item);
    loot.Call("AddCurrency", player, 100, 50);                  // 血岩, 重铸之尘
}
```

## 构建

```powershell
# 前置：PATH 上有带 SDK 的 dotnet（如 C:\Users\<你>\.dotnet\dotnet.exe）。
# tMLMod.targets 自动探测常见 Steam 库路径；若找不到，用 -p:LooteriaTMLPath 指定 tModLoader 安装目录
& dotnet build ".\Looteria.csproj" -c Debug -p:LooteriaTMLPath="<tModLoader 安装目录>"
# 产物：<tML 用户目录>\Mods\Looteria.tmod
```

> **换机构建**（L3）：`Looteria.csproj` 通过属性 `LooteriaTMLPath` 定位 `tMLMod.targets`
> （环境变量或命令行 `-p:LooteriaTMLPath=...`，缺省探测 `E:\SteamLibrary` / `D:\SteamLibrary` /
> `C:\Program Files (x86)\Steam` 等常见路径）。换机器/换 Steam 库路径时无需改文件，传参数即可。
> 只读编译可用 `-p:BuildMod=false`（跳过 .tmod 打包，适合语法检查）。

## 目录

- `Common/`：数据表、掷点引擎、效果、Global 钩子、UI、配置
- `Commands/`：测试命令
- `Localization/`：en-US（基准）+ zh-Hans
- `docs/`：玩法说明文档
