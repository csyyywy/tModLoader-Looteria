using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader.UI;
using Terraria.UI;
using global::Looteria.Common.Players;
using global::Looteria.Common.Systems;

namespace Looteria.Common.UI;

/// <summary>秘境页：自主选择层数（1 ~ 最佳层+1，通关上一关+力量达标即可）→ 开启（血岩+力量门槛）/ 进行中（计时/进度条/中止）。</summary>
public partial class LooteriaUIState
{
    private void BuildRift()
    {
        int top = 8;
        var player = Main.LocalPlayer;
        var lp = player.GetModPlayer<LooteriaPlayer>();
        AddSectionTitle(_content, T("RiftTitle"), C_Cyan, ref top);
        top += 4;

        if (!RiftSystem.RiftActive)
        {
            int maxSel = RiftSystem.BestLevel + 1;
            if (_riftLevel < 1 || _riftLevel > maxSel) _riftLevel = maxSel;

            // 顶部状态：最佳层 + 你的力量
            AddLabel(_content, $"{T("RiftBest")} {RiftSystem.BestLevel} · {T("RiftPower")} {lp.GearPower}",
                ref top, 0.85f, Color.LightGray);
            top += 28;

            // 层数选择器：− [层数] ＋
            var minus = new UITextPanel<string>("−", 0.9f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(8f, 0f),
                Width = new StyleDimension(40f, 0f),
                Height = new StyleDimension(30f, 0f)
            };
            minus.BackgroundColor = new Color(50, 52, 70);
            minus.BorderColor = new Color(70, 74, 96);
            minus.WithFadedMouseOver();
            minus.OnLeftClick += (_, _) => { if (_riftLevel > 1) { _riftLevel--; Rebuild(); } };
            _content.Append(minus);

            _content.Append(new UIText(T("RiftLevelPick").Replace("{0}", _riftLevel.ToString()).Replace("{1}", maxSel.ToString()), 0.8f)
            {
                Top = new StyleDimension(top + 5f, 0f),
                Left = new StyleDimension(56f, 0f),
                TextColor = Color.LightGray
            });

            var plus = new UITextPanel<string>("＋", 0.9f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(320f, 0f),
                Width = new StyleDimension(40f, 0f),
                Height = new StyleDimension(30f, 0f)
            };
            plus.BackgroundColor = new Color(50, 52, 70);
            plus.BorderColor = new Color(70, 74, 96);
            plus.WithFadedMouseOver();
            plus.OnLeftClick += (_, _) => { if (_riftLevel < maxSel) { _riftLevel++; Rebuild(); } };
            _content.Append(plus);
            top += 34;

            // 快捷跳转：最佳层 / 下一层
            var jumpBest = new UITextPanel<string>(T("RiftJumpBest"), 0.7f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(8f, 0f),
                Width = new StyleDimension(90f, 0f),
                Height = new StyleDimension(26f, 0f)
            };
            jumpBest.BackgroundColor = new Color(50, 52, 70);
            jumpBest.WithFadedMouseOver();
            jumpBest.OnLeftClick += (_, _) => { _riftLevel = Math.Max(1, RiftSystem.BestLevel); Rebuild(); };
            _content.Append(jumpBest);

            var jumpNext = new UITextPanel<string>(T("RiftJumpNext"), 0.7f)
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(106f, 0f),
                Width = new StyleDimension(90f, 0f),
                Height = new StyleDimension(26f, 0f)
            };
            jumpNext.BackgroundColor = new Color(50, 52, 70);
            jumpNext.WithFadedMouseOver();
            jumpNext.OnLeftClick += (_, _) => { _riftLevel = maxSel; Rebuild(); };
            _content.Append(jumpNext);
            top += 34;

            // 需求 / 消耗 / 现状
            int req = RiftSystem.RiftRequirement(_riftLevel);
            int cost = RiftSystem.RiftCost(_riftLevel);
            AddLabel(_content, $"{T("RiftReq")} {req} · {T("RiftCostLabel")} {cost}{T("Shards")} · {T("CurShards")}: {lp.BloodShards}",
                ref top, 0.8f, Color.LightGray);
            top += 30;

            bool okPower = lp.GearPower >= req;
            bool okCost = lp.BloodShards >= cost;
            AddButton(_content, $"{T("RiftStart")} ({cost}{T("Shards")})", ref top, () =>
            {
                // 多人：请求包上行（服务端校验/扣费/广播）；单机直通权威路径
                RiftSystem.RequestStartRift(_riftLevel);
                Rebuild();
            }, okPower && okCost ? new Color(50, 110, 70) : new Color(55, 58, 70));
            if (!okPower) AddLabel(_content, T("RiftNeedPower").Replace("{0}", (req - lp.GearPower).ToString()), ref top, 0.8f, C_Red);
            if (!okCost) AddLabel(_content, T("RiftNeedShards").Replace("{0}", (cost - lp.BloodShards).ToString()), ref top, 0.8f, C_Red);

            AddLabel(_content, T("RiftFreePick"), ref top, 0.7f, Color.Gray);
        }
        else
        {
            int secs = RiftSystem.TimerTicks / 60;
            AddLabel(_content, $"{T("RiftActive")} {RiftSystem.CurrentLevel} · {T("RiftTime")} {secs / 60}:{secs % 60:D2} · {T("RiftWave")} {RiftSystem.WaveIndex}",
                ref top, 0.9f, Color.Cyan);
            top += 30;
            _content.Append(new UIBar
            {
                Top = new StyleDimension(top, 0f),
                Left = new StyleDimension(8f, 0f),
                Width = new StyleDimension(320f, 0f),
                Height = new StyleDimension(18f, 0f),
                Fraction = RiftSystem.Progress / (float)RiftSystem.ProgressMax,
                FillColor = C_Cyan,
                Text = $"{RiftSystem.Progress}/{RiftSystem.ProgressMax}"
            });
            top += 36;
            AddButton(_content, T("RiftAbort"), ref top, () => { RiftSystem.RequestAbortRift(); Rebuild(); }, new Color(140, 50, 50));
        }
        _msgText = AddLabel(_content, "", ref top, 0.8f, Color.OrangeRed);
    }
}
