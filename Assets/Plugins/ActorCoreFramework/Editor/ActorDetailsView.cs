using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ActorCoreFramework.Editor
{
    /// <summary>
    /// 選択中のActorの詳細。状態、Tickの設定、Pawn / Controllerの関係、
    /// 利用者が宣言したフィールド、Componentを読み取り専用で表示する。
    ///
    /// 構造(どの行があるか)はActorかComponentの構成が変わったときだけ作り直し、
    /// 定期更新では各行の値だけを書き換える。毎回作り直すと折りたたみの状態や
    /// スクロール位置が失われるため。
    /// </summary>
    internal sealed class ActorDetailsView : ScrollView
    {
        const float LabelWidth = 130.0f;

        static readonly Dictionary<Type, FieldInfo[]> s_fieldCache = new();

        readonly Action<Actor> selectActor;

        /// <summary>定期更新で呼ぶ、各行の値の書き換え。</summary>
        readonly List<Action> updaters = new();

        /// <summary>構造を作ったときのComponentの並び。変わったら作り直す。</summary>
        readonly List<ActorComponent> builtComponents = new();

        readonly VisualElement messages = new();
        string builtMessages = string.Empty;

        Actor? actor;

        public ActorDetailsView(Action<Actor> selectActor)
        {
            this.selectActor = selectActor;

            style.flexGrow = 1.0f;
            contentContainer.style.paddingLeft = 8.0f;
            contentContainer.style.paddingRight = 8.0f;
            contentContainer.style.paddingTop = 6.0f;
            contentContainer.style.paddingBottom = 6.0f;

            Show(null);
        }

        public void Show(Actor? target)
        {
            actor = target;
            Rebuild();
        }

        public void Refresh()
        {
            if (actor == null) { return; }

            if (!SameComponents(actor))
            {
                Rebuild();
                return;
            }

            RefreshMessages();
            foreach (var update in updaters) { update(); }
        }


        // --- 構造 ---

        void Rebuild()
        {
            Clear();
            updaters.Clear();
            builtComponents.Clear();
            messages.Clear();
            builtMessages = string.Empty;

            var target = actor;
            if (target == null)
            {
                Add(new Label("左の一覧から Actor を選択してください。") { style = { opacity = 0.6f, whiteSpace = WhiteSpace.Normal } });
                return;
            }

            builtComponents.AddRange(target.Components);

            Add(CreateHeader(target));
            Add(messages);
            RefreshMessages();

            var state = Section("状態");
            AddRow(state, "State", () => target.State);
            AddRow(state, "IsPendingDestroy", () => target.IsPendingDestroy);
            AddRow(state, "World", () => target.World);
            Add(state);

            var tick = Section("Tick (PrimaryActorTick)");
            AddRow(tick, "CanEverTick", () => target.PrimaryActorTick.CanEverTick);
            AddRow(tick, "Enabled", () => target.PrimaryActorTick.Enabled);
            AddRow(tick, "Group", () => target.PrimaryActorTick.Group);
            AddRow(tick, "Priority", () => target.PrimaryActorTick.Priority);
            AddRow(tick, "Interval", () => target.PrimaryActorTick.Interval);
            Add(tick);

            if (target is Pawn pawn)
            {
                var section = Section("Pawn");
                AddRow(section, "Controller", () => pawn.Controller);
                // 破棄済みならMissingと表示される。Positionは例外になり、その型名が表示される
                AddRow(section, "Transform", () => pawn.Transform);
                AddRow(section, "Position", () => pawn.Position);
                Add(section);
            }

            if (target is Controller controller)
            {
                var section = Section("Controller");
                AddRow(section, "ControlledPawn", () => controller.ControlledPawn);
                Add(section);
            }

            var fields = GetUserFields(target.GetType(), typeof(Actor));
            if (fields.Length > 0)
            {
                var section = Section($"フィールド ({target.GetType().Name})");
                AddFieldRows(section, fields, target);
                Add(section);
            }

            var components = Section($"Components ({builtComponents.Count})");
            if (builtComponents.Count == 0)
            {
                components.Add(new Label("なし") { style = { opacity = 0.6f } });
            }

            foreach (var component in builtComponents)
            {
                components.Add(CreateComponent(component));
            }

            Add(components);
        }

        VisualElement CreateHeader(Actor target)
        {
            var header = new VisualElement { style = { marginBottom = 4.0f } };

            var title = new Label($"{target.Name}  #{target.Id}")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14.0f }
            };
            header.Add(title);
            updaters.Add(() => title.text = $"{target.Name}  #{target.Id}");

            header.Add(new Label(target.GetType().FullName) { style = { opacity = 0.6f } });

            if (target is Pawn pawn)
            {
                var select = new Button(() =>
                {
                    var gameObject = TryGet(() => pawn.Transform.gameObject);
                    if (gameObject == null) { return; }

                    Selection.activeGameObject = gameObject;
                    EditorGUIUtility.PingObject(gameObject);
                })
                {
                    text = "Hierarchy で GameObject を選択",
                    style = { alignSelf = Align.FlexStart, marginTop = 4.0f, marginLeft = 0.0f },
                };

                updaters.Add(() => select.SetEnabled(TryGet(() => pawn.Transform.gameObject) != null));
                header.Add(select);
            }

            return header;
        }

        VisualElement CreateComponent(ActorComponent component)
        {
            var foldout = new Foldout
            {
                text = component.GetType().Name,
                value = true,
                style = { marginTop = 2.0f },
            };

            AddRow(foldout, "Enabled", () => component.Enabled);
            AddRow(foldout, "HasBegunPlay", () => component.HasBegunPlay);
            AddRow(foldout, "IsPendingRemoval", () => component.IsPendingRemoval);

            AddFieldRows(foldout, GetUserFields(component.GetType(), typeof(ActorComponent)), component);

            return foldout;
        }

        static Foldout Section(string title)
        {
            var foldout = new Foldout { text = title, value = true, style = { marginTop = 6.0f } };
            foldout.Q<Toggle>().style.unityFontStyleAndWeight = FontStyle.Bold;

            return foldout;
        }

        void AddFieldRows(VisualElement parent, FieldInfo[] fields, object target)
        {
            foreach (var field in fields)
            {
                AddRow(parent, DisplayName(field), () =>
                {
                    try { return field.GetValue(target); }
                    catch (Exception e) { return new ReadError(e); }
                });
            }
        }

        void AddRow(VisualElement parent, string label, Func<object?> getValue)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, minHeight = 18.0f, alignItems = Align.Center }
            };

            row.Add(new Label(label)
            {
                style = { width = LabelWidth, minWidth = LabelWidth, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis }
            });

            var cell = new ValueCell(selectActor);
            row.Add(cell);
            parent.Add(row);

            void Update()
            {
                object? value;
                try { value = getValue(); }
                catch (Exception e) { value = new ReadError(e); }

                cell.Set(value);
            }

            Update();
            updaters.Add(Update);
        }

        bool SameComponents(Actor target)
        {
            var components = target.Components;
            if (components.Count != builtComponents.Count) { return false; }

            for (var i = 0; i < components.Count; i++)
            {
                if (!ReferenceEquals(components[i], builtComponents[i])) { return false; }
            }

            return true;
        }


        // --- 警告 ---

        void RefreshMessages()
        {
            if (actor == null) { return; }

            var found = CollectMessages(actor);

            // 同じ内容なら作り直さない(HelpBoxの点滅を避ける)
            var key = string.Join("\n", found.ConvertAll(m => m.text));
            if (key == builtMessages) { return; }

            builtMessages = key;
            messages.Clear();

            foreach (var (text, type) in found)
            {
                messages.Add(new HelpBox(text, type));
            }
        }

        /// <summary>
        /// 黙って何も起きない種類の誤りを拾う。例外にならないので、学習者は原因にたどり着きにくい。
        /// </summary>
        static List<(string text, HelpBoxMessageType type)> CollectMessages(Actor target)
        {
            var messages = new List<(string, HelpBoxMessageType)>();
            var tick = target.PrimaryActorTick;

            if (target.IsPendingDestroy && target.IsPlaying)
            {
                messages.Add(("破棄が予約されています。このフレームの PostTick の最後に破棄されます。", HelpBoxMessageType.Info));
            }

            if (!tick.CanEverTick && OverridesTick(target.GetType(), typeof(Actor)))
            {
                messages.Add((
                    "OnTick が実装されていますが、PrimaryActorTick.CanEverTick が false のため呼ばれません。\n" +
                    "コンストラクタで PrimaryActorTick.CanEverTick = true; を設定してください。",
                    HelpBoxMessageType.Warning));
            }

            if (!tick.CanEverTick)
            {
                foreach (var component in target.Components)
                {
                    if (!OverridesTick(component.GetType(), typeof(ActorComponent))) { continue; }

                    messages.Add((
                        $"{component.GetType().Name} の OnTick は呼ばれません。Component の Tick は所有者の Actor から配送されるため、" +
                        "所有者の PrimaryActorTick.CanEverTick を true にする必要があります。",
                        HelpBoxMessageType.Warning));
                }
            }

            if (tick.CanEverTick && !tick.Enabled)
            {
                messages.Add(("PrimaryActorTick.Enabled が false のため、Tick は一時停止中です。", HelpBoxMessageType.Info));
            }

            // GameObjectを壊してもWorldは気付かない。BindToの付け忘れで最も起きやすい
            if (target is Pawn pawn && target.IsPlaying && !target.IsPendingDestroy && pawn.Transform == null)
            {
                messages.Add((
                    "この Pawn の GameObject は破棄されていますが、Pawn は World に残っています。" +
                    "Tick で Transform に触れると MissingReferenceException になります。\n" +
                    "Spawn 時に .BindTo(destroyCancellationToken) で寿命を紐づけてください。",
                    HelpBoxMessageType.Error));
            }

            return messages;
        }

        static bool OverridesTick(Type type, Type baseType)
        {
            var method = type.GetMethod(
                "OnTick", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(float) }, null);

            return method != null && method.DeclaringType != baseType;
        }


        // --- フィールド ---

        /// <summary>
        /// 利用者が宣言したインスタンスフィールドを、基底側から順に集める。
        /// フレームワーク自身のフィールドは専用の行で表示するので含めない。
        /// </summary>
        static FieldInfo[] GetUserFields(Type type, Type stopAt)
        {
            if (s_fieldCache.TryGetValue(type, out var cached)) { return cached; }

            var frameworkAssembly = typeof(Actor).Assembly;
            var chain = new List<Type>();
            for (var t = type; t != null && t != stopAt; t = t.BaseType)
            {
                if (t.Assembly == frameworkAssembly) { continue; }
                chain.Add(t);
            }

            chain.Reverse();

            var fields = new List<FieldInfo>();
            foreach (var t in chain)
            {
                var declared = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in declared)
                {
                    // 自動プロパティの裏のフィールドは残し、それ以外のコンパイラ生成物は除く
                    if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !IsBackingField(field)) { continue; }

                    fields.Add(field);
                }
            }

            var result = fields.ToArray();
            s_fieldCache[type] = result;

            return result;
        }

        static bool IsBackingField(FieldInfo field) =>
            field.Name.StartsWith("<", StringComparison.Ordinal) &&
            field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal);

        static string DisplayName(FieldInfo field)
        {
            if (!IsBackingField(field)) { return field.Name; }

            // "<Hp>k__BackingField" → "Hp"
            var end = field.Name.IndexOf('>');
            return field.Name.Substring(1, end - 1);
        }

        /// <summary>値の読み取りで出た例外。文字列の値と区別して表示するために包む。</summary>
        sealed class ReadError
        {
            public readonly Exception Exception;
            public ReadError(Exception exception) => Exception = exception;
        }

        static T? TryGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }


        /// <summary>
        /// 値の種類に応じて表示を切り替えるセル。
        /// UnityのObjectはクリックでHierarchy / Projectに示せるようObjectFieldで、
        /// ActorはこのウィンドウでたどれるようリンクのButtonで表示する。
        /// </summary>
        sealed class ValueCell : VisualElement
        {
            readonly Label text = new();
            readonly ObjectField objectField = new();
            readonly Button link;

            Actor? linked;

            public ValueCell(Action<Actor> selectActor)
            {
                style.flexGrow = 1.0f;
                style.flexShrink = 1.0f;
                style.flexDirection = FlexDirection.Row;

                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.flexShrink = 1.0f;
                Add(text);

                // 読み取り専用。編集されたら元に戻す(無効化するとクリックでのPingもできなくなる)
                objectField.style.flexGrow = 1.0f;
                objectField.RegisterValueChangedCallback(e => objectField.SetValueWithoutNotify(e.previousValue));
                Add(objectField);

                link = new Button(() =>
                {
                    if (linked != null) { selectActor(linked); }
                })
                {
                    style = { marginLeft = 0.0f, unityTextAlign = TextAnchor.MiddleLeft },
                };
                Add(link);
            }

            public void Set(object? value)
            {
                switch (value)
                {
                    case Object unityObject when unityObject != null:
                        ShowOnly(objectField);
                        objectField.objectType = unityObject.GetType();
                        objectField.SetValueWithoutNotify(unityObject);
                        return;

                    // 参照は残っているが実体が破棄されたUnityのObject。学習者が最も戸惑う状態
                    case Object:
                        ShowText("Missing (破棄済み)", new Color(0.9f, 0.35f, 0.3f));
                        return;

                    case ReadError error:
                        linked = null;
                        ShowText($"<{error.Exception.GetType().Name}>", new Color(0.9f, 0.35f, 0.3f));
                        tooltip = error.Exception.Message;
                        return;

                    case Actor actor:
                        ShowOnly(link);
                        linked = actor;
                        link.text = actor.IsPendingDestroy || !actor.IsPlaying ? $"{actor} ({actor.State}, 破棄予約={actor.IsPendingDestroy})" : actor.ToString();
                        return;

                    default:
                        linked = null;
                        ShowText(Format(value), null);
                        return;
                }
            }

            void ShowText(string value, Color? color)
            {
                ShowOnly(text);
                text.text = value;
                text.style.color = color.HasValue ? new StyleColor(color.Value) : new StyleColor(StyleKeyword.Null);
            }

            void ShowOnly(VisualElement visible)
            {
                tooltip = string.Empty;
                text.style.display = visible == text ? DisplayStyle.Flex : DisplayStyle.None;
                objectField.style.display = visible == objectField ? DisplayStyle.Flex : DisplayStyle.None;
                link.style.display = visible == link ? DisplayStyle.Flex : DisplayStyle.None;
            }

            static string Format(object? value)
            {
                switch (value)
                {
                    case null: return "null";
                    case string s: return $"\"{s}\"";
                    case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                    case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                    case bool b: return b ? "true" : "false";
                    case ActorComponent component: return $"{component.GetType().Name} (Owner: {component.Owner})";
                    case World world: return world.Name;
                    case ICollection collection: return $"{FriendlyTypeName(value.GetType())} (Count = {collection.Count})";
                }

                try { return value.ToString() ?? string.Empty; }
                catch (Exception e) { return $"<{e.GetType().Name}>"; }
            }

            static string FriendlyTypeName(Type type)
            {
                if (!type.IsGenericType) { return type.Name; }

                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0) { name = name.Substring(0, tick); }

                return $"{name}<{string.Join(", ", Array.ConvertAll(type.GetGenericArguments(), FriendlyTypeName))}>";
            }
        }
    }
}
