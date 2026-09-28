using System;
using System.Linq;
using System.Collections;
using System.Globalization;
using CommandSystem;
using HsmAdapter;
using LabApi.Features.Wrappers;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;

namespace HsmAdapterProbe;

public sealed class ProbePlugin : Plugin
{
    internal static HintScope Main = null!, Other = null!;
    internal static int AutoCalls;
    public override string Name => "HsmAdapterProbe";
    public override string Author => "Local QA";
    public override string Description => "Test-only public adapter API walkthrough";
    public override Version RequiredApiVersion => new(1, 1, 0);
    public override void Enable() { Main = HsmAdapter.Hints.Acquire(Name); Other = HsmAdapter.Hints.Acquire(Name); }
    public override void Disable() { Main.Dispose(); Other.Dispose(); }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ProbeCommand : ICommand
{
    public string Command => "hsmadapterprobe";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Test-only HsmAdapter public API probe";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = "Usage: hsmadapterprobe ready [URI-escaped-plugin-name ...] | <player> <render|renew|persistent|shrink|dispose|clear>";
        if (!sender.CheckPermission(PlayerPermissions.ServerConsoleCommands, out _)) return false;
        if (arguments.Count >= 1 && arguments.At(0) == "ready")
        {
            // Remote admin splits on spaces, so encode each exact plugin name as one URI token.
            var names = arguments.Count == 1 ? new[] { "ReinforcementsSystem" } :
                Enumerable.Range(1, arguments.Count - 1)
                    .Select(i => Uri.UnescapeDataString(arguments.At(i))).ToArray();
            bool PluginReady(string name)
            {
                if (PluginLoader.EnabledPlugins.Any(p => p.Name == name)) return true;
                var loader = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Exiled.Loader");
                var plugins = loader?.GetType("Exiled.Loader.Loader")?.GetProperty("Plugins")?.GetValue(null) as IEnumerable;
                if (plugins == null || !PluginLoader.EnabledPlugins.Any(p => p.Name == "Exiled Loader")) return false;
                foreach (var plugin in plugins)
                {
                    if ((string?)plugin.GetType().GetProperty("Name")?.GetValue(plugin) != name) continue;
                    var config = plugin.GetType().GetProperty("Config")?.GetValue(plugin);
                    return config?.GetType().GetProperty("IsEnabled")?.GetValue(config) is true;
                }
                return false;
            }
            var missing = names.Where(n => !PluginReady(n)).ToArray();
            bool ready = HsmAdapter.Hints.IsReady && missing.Length == 0;
            response = ready ? "HSM_ADAPTER_READY " + string.Join(",", names) :
                "Adapter/HSM not ready or missing plugins: " + string.Join(",", missing);
            return ready;
        }
        if (arguments.Count == 1 && arguments.At(0) == "autocount")
        {
            response = "HSM_AUTO_CALLS " + ProbePlugin.AutoCalls;
            return ProbePlugin.AutoCalls > 0;
        }
        if (arguments.Count != 2 || !int.TryParse(arguments.At(0), out int id) || Player.Get(id) is not Player player) return false;
        void Show(string key, float y, TextRow[] rows, float duration = 0, float x = 0, float width = 900)
        {
            if (!ProbePlugin.Main.Show(player, key, new TextLayout(x, y, rows, maxWidth: width), duration))
                throw new InvalidOperationException("Adapter did not accept hint");
        }
        TextRow Row(string text, int size = 28, TextCase casing = TextCase.Preserve) => new(size, new TextSpan(text, casing: casing));
        switch (arguments.At(1))
        {
            case "screen":
                ProbePlugin.Main.Clear();
                ProbePlugin.Other.ShowRaw(player, "other", "<uppercase>另一个作用域</uppercase>", 0, 230);
                Show("portable", 300, new[] { Row("原有接口待替换") });
                void Expect(ScreenTextLayout layout, ScreenTextResult expected)
                {
                    var actual = ProbePlugin.Main.ShowScreen(player, "portable", layout, expected == ScreenTextResult.Shown ? 0 : 1);
                    if (actual != expected) throw new InvalidOperationException($"Expected {expected}, got {actual}");
                }
                var content = new[] { Row("保留大小写 ABC abc", 24), Row("转换大写 ABC abc", 24, TextCase.Upper), Row("转换小写 ABC abc", 24, TextCase.Lower) };
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), content), ScreenTextResult.Shown);
                Expect(new ScreenTextLayout(new ScreenRect(0, 300, 260, 400), content), ScreenTextResult.OutOfBounds);
                Expect(new ScreenTextLayout(new ScreenRect(-1, 300, 600, 110), content), ScreenTextResult.OutOfBounds);
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), content, alignment: TextAlignment.Left), ScreenTextResult.Unsupported);
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), content, overflow: TextOverflow.Clip), ScreenTextResult.Unsupported);
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), content, overflow: TextOverflow.Ellipsis), ScreenTextResult.Unsupported);
                Expect(new ScreenTextLayout(new ScreenRect(895, 300, 130, 110), content, overflow: TextOverflow.Reject), ScreenTextResult.DoesNotFit);
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 5), content), ScreenTextResult.DoesNotFit);
                Expect(new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), new[] { Row("literal <tag> {user} \\ path") }), ScreenTextResult.Unsupported);
                void Screen(string key, ScreenRect box, TextRow[] rows, VerticalAnchor anchor = VerticalAnchor.Top)
                {
                    if (ProbePlugin.Main.ShowScreen(player, key, new ScreenTextLayout(box, rows, verticalAlignment: anchor)) != ScreenTextResult.Shown)
                        throw new InvalidOperationException("Screen placement rejected: " + key);
                }
                Screen("left", new ScreenRect(430, 500, 260, 80), new[] { Row("左侧 Abc", 24) });
                Screen("right", new ScreenRect(1220, 500, 260, 80), new[] { Row("右侧 Abc", 24) });
                Screen("mixed", new ScreenRect(710, 440, 500, 100), new[] { Row("大标题", 48), Row("小字号正文", 18) }, VerticalAnchor.Middle);
                Screen("wrap", new ScreenRect(710, 560, 500, 120), new[] { Row("长文本按矩形宽度分行，大小写 AbCd 保持，最后一行靠近矩形底部。", 24) }, VerticalAnchor.Bottom);
                if (HsmAdapter.Hints.ScreenFeatures != (ScreenTextFeatures.CenterAlignment | ScreenTextFeatures.Wrap))
                    throw new InvalidOperationException("Unexpected HSM capabilities");
                break;
            case "screenraw":
                ProbePlugin.Main.ShowRaw(player, "portable", "<uppercase>原有接口替换为一行</uppercase>", 0, 320);
                break;
            case "screentimed":
                if (ProbePlugin.Main.ShowScreen(player, "portable", new ScreenTextLayout(new ScreenRect(660, 300, 600, 110), new[] { Row("屏幕接口刷新期限 ABC abc", 24) }), 10) != ScreenTextResult.Shown)
                    throw new InvalidOperationException("Screen renewal rejected");
                break;
            case "edgeprobe":
                ProbePlugin.Main.Clear();
                const string edge = "<uppercase>出生保护 ABC </uppercase><lowercase>abc</lowercase><uppercase> 12秒</uppercase>";
                ProbePlugin.Main.ShowRaw(player, "oldleft", edge, -1600, 300);
                ProbePlugin.Main.ShowRaw(player, "oldright", edge, 1600, 350);
                ProbePlugin.Main.ShowRaw(player, "nobrleft", "<nobr>" + edge + "</nobr>", -1600, 450);
                ProbePlugin.Main.ShowRaw(player, "nobrright", "<nobr>" + edge + "</nobr>", 1600, 500);
                ProbePlugin.Main.ShowRaw(player, "cellleft", "<nobr><mspace=24>" + edge + "</mspace></nobr>", -1456, 600);
                ProbePlugin.Main.ShowRaw(player, "cellright", "<nobr><mspace=24>" + edge + "</mspace></nobr>", 1456, 650);
                break;
            case "hsmcompat":
                if (!ProbePlugin.Main.ShowHsm(player, "hsmcompat", new HsmHintLayout(
                    "<color=#63D9FF>兼容提示 ABC abc</color>", 0, 400, 26,
                    VerticalAnchor.Middle, HsmHorizontalAlignment.Center, HsmSyncSpeed.Fast,
                    lineHeight: 2), 5)) throw new InvalidOperationException("HSM compatibility unavailable");
                break;
            case "hsmdefault":
                if (!ProbePlugin.Main.ShowHsm(player, "hsmcompat", new HsmHintLayout(
                    "恢复默认同步", 0, 400, 26, VerticalAnchor.Middle), 0))
                    throw new InvalidOperationException("HSM default sync unavailable");
                break;
            case "hsmauto":
                ProbePlugin.AutoCalls = 0;
                if (!ProbePlugin.Main.ShowHsmAutoText(player, "hsmauto",
                    () => "自动更新 " + (++ProbePlugin.AutoCalls),
                    new HsmHintLayout("", 0, 520, 24, syncSpeed: HsmSyncSpeed.Normal)))
                    throw new InvalidOperationException("HSM AutoText unavailable");
                break;
            case "hsmdynamic":
                if (!ProbePlugin.Main.ShowHsmDynamic(player, "hsmdynamic",
                    new HsmDynamicLayout("动态目标", 350, 600)))
                    throw new InvalidOperationException("HSM DynamicHint unavailable");
                break;
            case "hsmleft":
                ProbePlugin.Main.ShowHsm(player, "leave-fixed", new HsmHintLayout("离开清理", 0, 470));
                ProbePlugin.Main.ShowHsmAutoText(player, "leave-auto", () => "离开回调",
                    new HsmHintLayout("", 0, 520));
                ProbePlugin.Main.ShowHsmDynamic(player, "leave-dynamic",
                    new HsmDynamicLayout("离开动态", 350, 600));
                break;
            case "render":
                ProbePlugin.Main.Clear();
                ProbePlugin.Other.ShowRaw(player, "other", "<color=#63D9FF>原有提示 ABC abc</color>", 0, 200);
                Show("case", 280, new[] { Row("保留大小写 ABC abc 出生保护"), Row("转换大写 ABC abc", casing: TextCase.Upper), Row("转换小写 ABC abc", casing: TextCase.Lower) });
                Show("mixed", 460, new[] { Row("大标题", 48), Row("小字号正文不会重叠", 18) });
                Show("wrap", 650, new[] { Row("长文本按安全宽度分行而不是突然跳到屏幕中央，大小写 AbCd 保持。", 24) }, x: -1600, width: 500);
                break;
            case "fractional":
                // Disposable host only: exercise comma-decimal formatting on the server and async parser.
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
                if (!ProbePlugin.Main.Show(player, "fractional", new TextLayout(1040.5f, 400.75f,
                    new[] { Row("小数坐标 ABC abc", 25), Row("间距和锚点保持正确", 17) },
                    VerticalAnchor.Middle, maxWidth: 800, rowGap: 8.5f)))
                    throw new InvalidOperationException("Adapter did not accept fractional layout");
                break;
            case "renew":
                ProbePlugin.Main.Clear();
                Show("renew", 520, new[] { Row("定时提示等待刷新") }, 5);
                break;
            case "persistent":
                Show("renew", 520, new[] { Row("刷新后持续显示 ABC abc") });
                break;
            case "same":
                Show("renew", 520, new[] { Row("定时提示等待刷新") }, 5);
                break;
            case "shrink":
                Show("case", 280, new[] { Row("三行替换为一行") });
                break;
            case "timed":
                ProbePlugin.Main.Clear();
                Show("timed", 520, new[] { Row("两秒后到期") }, 2);
                break;
            case "anchor":
                ProbePlugin.Main.ShowRaw(player, "anchor", "<uppercase>锚点跟随更新</uppercase>", 0, 460, 48, VerticalAnchor.Bottom);
                break;
            case "retop":
                ProbePlugin.Main.ShowRaw(player, "anchor", "<uppercase>锚点跟随更新</uppercase>", 0, 460, 48, VerticalAnchor.Top);
                break;
            case "dispose": ProbePlugin.Main.Dispose(); break;
            case "clear": ProbePlugin.Other.Clear(); break;
            default: return false;
        }
        response = "ADAPTER_ACTION_OK " + arguments.At(1);
        return true;
    }
}
