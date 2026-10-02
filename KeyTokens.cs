using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Hints;
using LabApi.Features.Console;
using Mirror;

namespace HsmAdapter;

/// <summary>
/// Native keybind names inside HSM text. HSM strips braces from hint text and sends every hint with one empty
/// string parameter, so a key name cannot travel through it as a placeholder. A key token is a private-use
/// marker instead. For a player whose text carries tokens, the adapter swaps HSM's built-in network output for
/// one that sends the same <c>TextHint</c> but turns each marker into a native
/// <see cref="SSKeybindHintParameter"/>, which the client renders as its own bound key or its localized
/// "key not assigned". The server never learns the binding.
/// </summary>
internal static class KeyTokens
{
    internal const char Open = '';
    internal const char Close = '';
    private const string KeyFormat = "[{0}]";

    private static readonly ConditionalWeakTable<object, object> Installed = new();
    private static Type? _outputType;
    private static Type? _outputInterface;
    private static OutputApi? _api;
    private static Type? _apiDisplayType;

    internal static string Marker(int settingId) =>
        Open + settingId.ToString(CultureInfo.InvariantCulture) + Close;

    /// <summary>True once this player's display sends through the token-aware output.</summary>
    internal static bool Ensure(HsmBackend backend, ReferenceHub hub)
    {
        if (hub == null) return false;
        object display;
        try { display = backend.Display(hub); }
        catch { return false; }
        if (Installed.TryGetValue(display, out _)) return true;
        OutputApi? api = Api(display.GetType());
        if (api == null) return false;

        object? output = null;
        try
        {
            output = Activator.CreateInstance(OutputType(api.Interface),
                new Action<object>(arg => Send(hub, api.Content.GetValue(arg) as string)));
            // Add first: a failed removal must never leave the player without any output.
            api.Add.Invoke(display, new[] { output! });
            api.RemoveBuiltIn.Invoke(display, null);
            Installed.Add(display, output!);
            return true;
        }
        catch (Exception ex)
        {
            if (output != null)
            {
                try { api.Remove.Invoke(display, new[] { output }); } catch { /* the built-in output still sends */ }
            }

            Logger.Warn("[HsmAdapter] Key tokens unavailable for this display: " + ex.GetBaseException().Message);
            return false;
        }
    }

    /// <summary>Mirrors HSM's own output (one empty string parameter, full alpha, long duration) plus key parameters.</summary>
    private static void Send(ReferenceHub hub, string? content)
    {
        NetworkConnection? connection = hub != null ? hub.connectionToClient : null;
        if (connection is not { isReady: true }) return;
        try
        {
            var parameters = new List<HintParameter> { new StringHintParameter(string.Empty) };
            string text = Rewrite(content ?? string.Empty, parameters);
            connection.Send(new HintMessage(new TextHint(text, parameters.ToArray(), new HintEffect[] { new AlphaEffect(1) }, 99999f)));
        }
        catch (Exception ex)
        {
            Logger.Error("[HsmAdapter] Key-token hint send failed: " + ex);
        }
    }

    /// <summary>Replaces markers with <c>{n}</c> placeholders and appends one keybind parameter per distinct id.</summary>
    internal static string Rewrite(string content, List<HintParameter> parameters)
    {
        if (content.IndexOf(Open) < 0 && content.IndexOf('{') < 0 && content.IndexOf('}') < 0) return content;
        var indexes = new Dictionary<int, int>();
        var sb = new StringBuilder(content.Length);
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '{' || c == '}') continue; // HSM strips braces; keep the format string valid regardless.
            if (c != Open) { sb.Append(c); continue; }
            int end = content.IndexOf(Close, i + 1);
            if (end < 0) continue;
            if (int.TryParse(content.Substring(i + 1, end - i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                if (!indexes.TryGetValue(id, out int index))
                {
                    index = parameters.Count;
                    parameters.Add(new SSKeybindHintParameter(id, KeyFormat));
                    indexes[id] = index;
                }

                sb.Append('{').Append(index.ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            i = end;
        }

        return sb.ToString();
    }

    /// <summary>The output API for this HSM build, bound once per display type; null when it lacks one.</summary>
    private static OutputApi? Api(Type displayType)
    {
        if (_apiDisplayType != displayType)
        {
            _apiDisplayType = displayType;
            _api = OutputApi.Bind(displayType);
            if (_api == null) Logger.Warn("[HsmAdapter] This HintServiceMeow build has no display-output API; key tokens fall back to plain text.");
        }

        return _api;
    }

    /// <summary>
    /// HSM's per-player output API, read from the player-display type and its assembly: <c>IDisplayOutput</c>,
    /// <c>DisplayOutputArg.Content</c>, <c>PlayerDisplay.AddDisplayOutput</c>/<c>RemoveDisplayOutput</c> and the
    /// built-in <c>ScpslDisplayOutput</c>. Optional, and kept out of <see cref="HsmBackend"/>: an HSM build without
    /// it leaves key tokens off while every other adapter feature keeps working.
    /// </summary>
    private sealed class OutputApi
    {
        private OutputApi(Type outputInterface, PropertyInfo content, MethodInfo add, MethodInfo remove, MethodInfo removeBuiltIn)
        {
            Interface = outputInterface;
            Content = content;
            Add = add;
            Remove = remove;
            RemoveBuiltIn = removeBuiltIn;
        }

        public Type Interface { get; }
        public PropertyInfo Content { get; }
        public MethodInfo Add { get; }
        public MethodInfo Remove { get; }
        public MethodInfo RemoveBuiltIn { get; }

        public static OutputApi? Bind(Type displayType)
        {
            try
            {
                Assembly assembly = displayType.Assembly;
                Type? outputInterface = assembly.GetType("HintServiceMeow.Core.Interface.IDisplayOutput");
                Type? builtIn = assembly.GetType("HintServiceMeow.Core.Utilities.UnityAdaptors.ScpslDisplayOutput");
                PropertyInfo? content = assembly.GetType("HintServiceMeow.Core.Models.Arguments.DisplayOutputArg")?.GetProperty("Content");
                if (outputInterface == null || builtIn == null || content == null) return null;
                MethodInfo? add = displayType.GetMethod("AddDisplayOutput", new[] { outputInterface });
                MethodInfo? remove = displayType.GetMethod("RemoveDisplayOutput", new[] { outputInterface });
                MethodInfo? removeBuiltIn = displayType.GetMethods().FirstOrDefault(m => m.Name == "RemoveDisplayOutput" &&
                    m.IsGenericMethodDefinition && m.GetParameters().Length == 0)?.MakeGenericMethod(builtIn);
                return add == null || remove == null || removeBuiltIn == null
                    ? null
                    : new OutputApi(outputInterface, content, add, remove, removeBuiltIn);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Emits one class implementing HSM's output interface that forwards to a callback.</summary>
    private static Type OutputType(Type outputInterface)
    {
        if (_outputType != null && _outputInterface == outputInterface) return _outputType;
        MethodInfo show = outputInterface.GetMethod("ShowHint") ?? throw new MissingMethodException("IDisplayOutput.ShowHint");
        Type argType = show.GetParameters()[0].ParameterType;
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("HsmAdapter.KeyTokenOutput"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("HsmAdapter.KeyTokenOutput").DefineType(
            "HsmAdapter.KeyTokenOutput", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(object), new[] { outputInterface });
        var callback = type.DefineField("_callback", typeof(Action<object>), FieldAttributes.Private | FieldAttributes.InitOnly);

        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(Action<object>) });
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, callback);
        il.Emit(OpCodes.Ret);

        var method = type.DefineMethod("ShowHint",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void), new[] { argType });
        il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callback);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Action<object>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, show);

        _outputInterface = outputInterface;
        return _outputType = type.CreateType();
    }
}
