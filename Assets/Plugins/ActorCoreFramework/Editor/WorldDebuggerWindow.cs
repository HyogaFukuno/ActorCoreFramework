using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ActorCoreFramework.Editor
{
    /// <summary>
    /// 生きているWorldと、そこにSpawnされているActor / Componentを一覧するウィンドウ。
    ///
    /// ActorはGameObjectではないためHierarchyにもInspectorにも現れない。
    /// 「何がいくつ存在して、どの状態にあるか」をここで目視できるようにする。
    /// あわせて、CanEverTickの設定漏れやWorldLoopへの登録漏れなど、
    /// 黙って何も起きない種類の誤りを警告として表示する。
    /// </summary>
    public sealed class WorldDebuggerWindow : EditorWindow
    {
        /// <summary>表示の更新間隔(ミリ秒)。毎フレーム作り直すほどの鮮度は要らない。</summary>
        const long RefreshIntervalMs = 200;

        readonly List<World> worlds = new();
        readonly List<Actor> rows = new();

        World? selectedWorld;
        Actor? selectedActor;
        string searchText = string.Empty;

        DropdownField worldField = null!;
        Label countLabel = null!;
        VisualElement worldMessages = null!;
        VisualElement emptyState = null!;
        TwoPaneSplitView splitView = null!;
        MultiColumnListView actorList = null!;
        ActorDetailsView details = null!;

        [MenuItem("Window/Actor Core Framework/World Debugger")]
        public static void Open()
        {
            var window = GetWindow<WorldDebuggerWindow>();
            window.titleContent = new GUIContent("World Debugger");
            window.minSize = new Vector2(560.0f, 300.0f);
            window.Show();
        }

        void OnEnable()
        {
            // HierarchyでGameObjectを選ぶと、それを持つPawnをこちらでも選ぶ
            Selection.selectionChanged += OnEditorSelectionChanged;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnEditorSelectionChanged;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;

            root.Add(CreateToolbar());

            worldMessages = new VisualElement();
            root.Add(worldMessages);

            emptyState = new Label(
                "生きている World がありません。\n" +
                "Play モードに入り、new World() で World を生成すると、ここに表示されます。")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    flexGrow = 1.0f,
                    whiteSpace = WhiteSpace.Normal,
                    opacity = 0.6f,
                }
            };
            root.Add(emptyState);

            splitView = new TwoPaneSplitView(0, 320.0f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1.0f;
            root.Add(splitView);

            actorList = CreateActorList();
            splitView.Add(actorList);

            details = new ActorDetailsView(SelectActor);
            splitView.Add(details);

            Refresh();
            root.schedule.Execute(Refresh).Every(RefreshIntervalMs);
        }

        Toolbar CreateToolbar()
        {
            var toolbar = new Toolbar();

            worldField = new DropdownField { style = { minWidth = 140.0f } };
            worldField.RegisterValueChangedCallback(_ =>
            {
                var index = worldField.index;
                SelectWorld(index >= 0 && index < worlds.Count ? worlds[index] : null);
            });
            toolbar.Add(worldField);

            var search = new ToolbarSearchField { style = { flexGrow = 1.0f, flexShrink = 1.0f } };
            search.RegisterValueChangedCallback(e =>
            {
                searchText = e.newValue ?? string.Empty;
                Refresh();
            });
            toolbar.Add(search);

            countLabel = new Label { style = { unityTextAlign = TextAnchor.MiddleRight, marginLeft = 6.0f, marginRight = 6.0f } };
            toolbar.Add(countLabel);

            return toolbar;
        }

        MultiColumnListView CreateActorList()
        {
            var list = new MultiColumnListView
            {
                itemsSource = rows,
                fixedItemHeight = 20.0f,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                style = { flexGrow = 1.0f },
            };

            list.columns.Add(TextColumn("id", "Id", 44.0f, false, a => a.Id.ToString()));
            list.columns.Add(TextColumn("name", "Name", 140.0f, true, a => a.Name));
            list.columns.Add(TextColumn("type", "Type", 120.0f, true, a => a.GetType().Name));
            list.columns.Add(TextColumn("state", "State", 90.0f, false, DescribeState));
            list.columns.Add(TextColumn("tick", "Tick", 90.0f, false, DescribeTick));

            list.selectionChanged += selection =>
            {
                foreach (var item in selection)
                {
                    SelectActorWithoutNotify((Actor)item);
                    return;
                }

                SelectActorWithoutNotify(null);
            };

            return list;
        }

        Column TextColumn(string name, string title, float width, bool stretchable, Func<Actor, string> text)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                stretchable = stretchable,
                makeCell = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 4.0f } },
                bindCell = (element, index) =>
                {
                    var label = (Label)element;
                    if (index < 0 || index >= rows.Count) { return; }

                    var actor = rows[index];
                    label.text = text(actor);

                    // 破棄の予約から実際の破棄までは一覧に残るので、見た目で区別する
                    label.style.opacity = actor.IsPendingDestroy || !actor.IsPlaying ? 0.45f : 1.0f;
                },
            };
        }


        // --- 更新 ---

        void Refresh()
        {
            if (actorList == null) { return; }

            RefreshWorlds();
            RefreshWorldMessages();
            RefreshRows();

            // 選択中のActorがWorldから消えたら詳細も閉じる
            if (selectedActor != null && !rows.Contains(selectedActor) && !IsInWorld(selectedActor))
            {
                SelectActorWithoutNotify(null);
            }

            details.Refresh();
        }

        void RefreshWorlds()
        {
            WorldRegistry.CopyTo(worlds);

            if (selectedWorld == null || !worlds.Contains(selectedWorld))
            {
                selectedWorld = worlds.Count > 0 ? worlds[0] : null;
                SelectActorWithoutNotify(null);
            }

            var names = new List<string>(worlds.Count);
            foreach (var world in worlds) { names.Add(ChoiceName(world)); }

            // 選択肢が変わったときだけ差し替える。開いているドロップダウンを閉じないため
            if (!SequenceEqual(worldField.choices, names)) { worldField.choices = names; }

            worldField.SetValueWithoutNotify(selectedWorld != null ? ChoiceName(selectedWorld) : "(World なし)");
            worldField.SetEnabled(worlds.Count > 1);

            var hasWorld = selectedWorld != null;
            emptyState.style.display = hasWorld ? DisplayStyle.None : DisplayStyle.Flex;
            splitView.style.display = hasWorld ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshWorldMessages()
        {
            worldMessages.Clear();

            var world = selectedWorld;
            if (world == null) { return; }

            if (!EditorApplication.isPlaying)
            {
                var box = new HelpBox(
                    $"{world.Name} は Play モードの外でも生きています。World.Dispose() が呼ばれていません。\n" +
                    "World を生成した MonoBehaviour の OnDestroy などで Dispose してください。" +
                    "Dispose しないと Actor に EndPlay が届きません。",
                    HelpBoxMessageType.Warning);

                var dispose = new Button(() =>
                {
                    try { world.Dispose(); }
                    catch (Exception e) { Debug.LogException(e); }
                    Refresh();
                }) { text = "この World を Dispose" };
                box.Add(dispose);

                worldMessages.Add(box);
                return;
            }

            if (!WorldLoop.IsRegistered(world))
            {
                worldMessages.Add(new HelpBox(
                    $"{world.Name} は WorldLoop に登録されていないため、Tick が配送されません。\n" +
                    "World を生成した後に WorldLoop.Register(world) を呼んでください。" +
                    "(World.Tick などを自分で呼んで回している場合は、この警告は無視して構いません)",
                    HelpBoxMessageType.Warning));
            }
        }

        void RefreshRows()
        {
            rows.Clear();

            var world = selectedWorld;
            if (world != null)
            {
                var actors = world.Actors;
                for (var i = 0; i < actors.Count; i++)
                {
                    var actor = actors[i];
                    if (Matches(actor)) { rows.Add(actor); }
                }
            }

            actorList.RefreshItems();

            countLabel.text = world == null ? string.Empty : $"Actor: {CountAlive(world)}";

            // 行の並びが変わっても選択は同じActorに付いていく
            var index = selectedActor != null ? rows.IndexOf(selectedActor) : -1;
            if (index >= 0)
            {
                if (actorList.selectedIndex != index) { actorList.SetSelectionWithoutNotify(new[] { index }); }
            }
            else if (actorList.selectedIndex >= 0)
            {
                actorList.ClearSelection();
            }
        }

        bool Matches(Actor actor)
        {
            if (string.IsNullOrEmpty(searchText)) { return true; }

            return Contains(actor.Name, searchText) ||
                   Contains(actor.GetType().Name, searchText) ||
                   Contains(actor.Id.ToString(), searchText);
        }

        static bool Contains(string text, string value) =>
            text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        static int CountAlive(World world)
        {
            var count = 0;
            var actors = world.Actors;
            for (var i = 0; i < actors.Count; i++)
            {
                if (actors[i].IsPlaying && !actors[i].IsPendingDestroy) { count++; }
            }

            return count;
        }

        bool IsInWorld(Actor actor)
        {
            if (selectedWorld == null) { return false; }

            var actors = selectedWorld.Actors;
            for (var i = 0; i < actors.Count; i++)
            {
                if (ReferenceEquals(actors[i], actor)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// ドロップダウンの表示名。選択は表示名から引き戻されるので、同名のWorldはIdで区別する。
        /// </summary>
        string ChoiceName(World world)
        {
            foreach (var other in worlds)
            {
                if (!ReferenceEquals(other, world) && other.Name == world.Name) { return $"{world.Name} (#{world.Id})"; }
            }

            return world.Name;
        }

        static bool SequenceEqual(List<string>? a, List<string> b)
        {
            if (a == null || a.Count != b.Count) { return false; }

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) { return false; }
            }

            return true;
        }


        // --- 選択 ---

        void SelectWorld(World? world)
        {
            if (ReferenceEquals(selectedWorld, world)) { return; }

            selectedWorld = world;
            SelectActorWithoutNotify(null);
            Refresh();
        }

        /// <summary>詳細表示のリンクなどから、別のActorへ選択を移す。</summary>
        void SelectActor(Actor actor)
        {
            if (actor.World != null && !ReferenceEquals(actor.World, selectedWorld))
            {
                selectedWorld = actor.World;
            }

            // 検索で隠れていると選べないので、絞り込みを外す
            if (!Matches(actor))
            {
                searchText = string.Empty;
                var search = rootVisualElement.Q<ToolbarSearchField>();
                search?.SetValueWithoutNotify(string.Empty);
            }

            SelectActorWithoutNotify(actor);
            Refresh();

            var index = rows.IndexOf(actor);
            if (index >= 0) { actorList.ScrollToItem(index); }
        }

        void SelectActorWithoutNotify(Actor? actor)
        {
            if (ReferenceEquals(selectedActor, actor)) { return; }

            selectedActor = actor;
            details?.Show(actor);
        }

        void OnEditorSelectionChanged()
        {
            var gameObject = Selection.activeGameObject;
            if (gameObject == null || selectedWorld == null) { return; }

            var actors = selectedWorld.Actors;
            for (var i = 0; i < actors.Count; i++)
            {
                if (actors[i] is Pawn pawn && pawn.Transform != null && pawn.Transform.gameObject == gameObject)
                {
                    SelectActor(pawn);
                    return;
                }
            }
        }


        // --- 表示用の文字列 ---

        static string DescribeState(Actor actor)
        {
            if (actor.IsPendingDestroy && actor.IsPlaying) { return "破棄予約"; }

            return actor.State switch
            {
                ActorState.Created => "未登録",
                ActorState.Playing => "Playing",
                ActorState.Ended => "Ended",
                ActorState.Disposed => "Disposed",
                _ => actor.State.ToString(),
            };
        }

        static string DescribeTick(Actor actor)
        {
            var tick = actor.PrimaryActorTick;
            if (!tick.CanEverTick) { return "—"; }

            return tick.Enabled ? tick.Group.ToString() : $"{tick.Group} (停止)";
        }
    }
}
