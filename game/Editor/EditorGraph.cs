using System;
using System.Collections.Generic;
using GicEngine.Script;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Editor;

/// <summary>
/// Граф истории в стиле Blueprint (ответ 62): каждая метка — узел, переходы jump/call и действия иконок — провода.
/// Граф строится из текста сценария, поэтому всегда совпадает с ним. Положение узлов запоминается, пока окно открыто.
/// </summary>
public partial class EditorGraph : GraphEdit
{
    /// <summary>Выбран узел (метка).</summary>
    public event Action<string> LabelSelected;

    /// <summary>Двойной клик по узлу — перейти к метке в тексте.</summary>
    public event Action<string> LabelActivated;

    // Расстояние между колонками и строками при автоматической раскладке.
    private const float ColumnStep = 460f;
    private const float RowStep = 230f;

    private readonly Dictionary<string, Vector2> _positions = new();

    public EditorGraph()
    {
        ShowGrid = true;
        MinimapEnabled = true;
        RightDisconnects = false;
        ShowArrangeButton = false;
        NodeSelected += node => LabelSelected?.Invoke(node.Name);
        GuiInput += OnGraphInput;
    }

    /// <summary>Двойной клик по выделенному узлу открывает метку в тексте.</summary>
    private void OnGraphInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left }) return;
        foreach (var child in GetChildren())
        {
            if (child is GraphNode { Selected: true } node)
            {
                LabelActivated?.Invoke(node.Name);
                return;
            }
        }
    }

    /// <summary>Перестроить граф по истории. Старые позиции узлов сохраняются, новые раскладываются по глубине от старта.</summary>
    public void Build(StoryProject project)
    {
        // Запоминаем, куда автор перетащил узлы.
        foreach (var child in GetChildren())
        {
            if (child is GraphNode n) _positions[n.Name] = n.PositionOffset;
        }
        ClearConnections();
        foreach (var child in GetChildren())
        {
            if (child is GraphNode n)
            {
                RemoveChild(n);
                n.QueueFree();
            }
        }
        if (project == null) return;

        var edges = CollectEdges(project);
        var layout = AutoLayout(project, edges);

        foreach (var label in project.Labels.Values)
        {
            var node = new GraphNode
            {
                Name = label.Name,
                Title = label.Name,
                PositionOffset = _positions.TryGetValue(label.Name, out var p) ? p : layout[label.Name],
                CustomMinimumSize = new Vector2(300, 0),
                TooltipText = $"{label.File.GetFile()}:{label.Line}",
            };
            ColorTitle(node, Category(project, label));
            AddChild(node);

            // Строка 0: краткое содержание метки, вход слева и «провал» в следующую метку справа.
            var summary = UiKit.Label(Summary(label), Fonts.Body, 15, Palette.EdMuted);
            summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            summary.CustomMinimumSize = new Vector2(280, 0);
            node.AddChild(summary);
            var targets = edges[label.Name];
            var hasNext = targets.Exists(t => t.Kind == "next");
            node.SetSlot(0, true, 0, Colors.White, hasNext, 0, new Color("9e9e9e"));

            // Остальные строки — по одному выходу на каждый переход.
            var slot = 1;
            foreach (var edge in targets)
            {
                if (edge.Kind == "next") continue;
                var row = UiKit.Label($"{KindLabel(edge.Kind)} → {edge.Target}", Fonts.Mono, 14, EdgeColor(edge.Kind));
                row.HorizontalAlignment = HorizontalAlignment.Right;
                node.AddChild(row);
                node.SetSlot(slot++, false, 0, Colors.White, true, 0, EdgeColor(edge.Kind));
            }
        }

        // Провода. Номер выхода — порядковый номер среди включённых правых слотов узла.
        foreach (var label in project.Labels.Values)
        {
            var port = 0;
            var targets = edges[label.Name];
            var next = targets.Find(t => t.Kind == "next");
            if (next.Target != null)
            {
                if (project.Labels.ContainsKey(next.Target)) ConnectNode(label.Name, port, next.Target, 0);
                port++;
            }
            foreach (var edge in targets)
            {
                if (edge.Kind == "next") continue;
                if (project.Labels.ContainsKey(edge.Target)) ConnectNode(label.Name, port, edge.Target, 0);
                port++;
            }
        }
    }

    /// <summary>Выделить узел и прокрутить к нему.</summary>
    public void Focus(string label)
    {
        foreach (var child in GetChildren())
        {
            if (child is not GraphNode n) continue;
            n.Selected = n.Name == label;
            if (n.Selected) ScrollOffset = n.PositionOffset * Zoom - Size / 2 + n.Size * Zoom / 2;
        }
    }

    // ---------------------------------------------------------------- переходы и раскладка

    private readonly record struct Edge(string Target, string Kind);

    /// <summary>
    /// Заканчивается ли метка безусловным переходом (jump, return, end, os wait).
    /// Если нет — выполнение «проваливается» в следующую метку файла.
    /// </summary>
    public static bool EndsHard(LabelDef label)
    {
        for (var i = label.Code.Count - 1; i >= 0; i--)
        {
            var instr = label.Code[i];
            if (instr.Op == OpCode.Goto) continue;
            return instr.Command is ReturnCommand or EndCommand or OsCommand { Action: "wait" } or JumpCommand { IsCall: false };
        }
        return false;
    }

    /// <summary>Все переходы из каждой метки: jump, call, действия иконок/файлов и «провал» в следующую метку.</summary>
    private static Dictionary<string, List<Edge>> CollectEdges(StoryProject project)
    {
        var result = new Dictionary<string, List<Edge>>();
        foreach (var label in project.Labels.Values)
        {
            var list = new List<Edge>();
            var seen = new HashSet<string>();
            var endsHard = false;
            foreach (var instr in label.Code)
            {
                endsHard = false;
                if (instr.Op != OpCode.Exec) continue;
                switch (instr.Command)
                {
                    case JumpCommand j when j.Target != null:
                        if (seen.Add((j.IsCall ? "call:" : "jump:") + j.Target)) list.Add(new Edge(j.Target, j.IsCall ? "call" : "jump"));
                        endsHard = !j.IsCall;
                        break;
                    case OsCommand os when os.Opt("action") != null:
                        if (seen.Add("action:" + os.Opt("action"))) list.Add(new Edge(os.Opt("action"), "action"));
                        break;
                    case ReturnCommand or EndCommand:
                        endsHard = true;
                        break;
                    case OsCommand { Action: "wait" }:
                        endsHard = true;
                        break;
                }
            }
            // Метка без завершающего jump/return/end «проваливается» в следующую.
            if (!endsHard && label.FallThrough != null) list.Insert(0, new Edge(label.FallThrough, "next"));
            result[label.Name] = list;
        }
        return result;
    }

    /// <summary>Раскладка по глубине: старт слева, каждый переход — колонка правее.</summary>
    private static Dictionary<string, Vector2> AutoLayout(StoryProject project, Dictionary<string, List<Edge>> edges)
    {
        var depth = new Dictionary<string, int>();
        var queue = new Queue<string>();
        if (project.Labels.ContainsKey(project.Meta.Start))
        {
            depth[project.Meta.Start] = 0;
            queue.Enqueue(project.Meta.Start);
        }
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            foreach (var e in edges[name])
            {
                if (!project.Labels.ContainsKey(e.Target) || depth.ContainsKey(e.Target)) continue;
                depth[e.Target] = depth[name] + 1;
                queue.Enqueue(e.Target);
            }
        }
        // Недостижимые от старта метки (главы, служебные) — в отдельной колонке справа.
        var maxDepth = 0;
        foreach (var d in depth.Values) maxDepth = Math.Max(maxDepth, d);
        foreach (var name in project.Labels.Keys)
        {
            if (!depth.ContainsKey(name)) depth[name] = maxDepth + 1;
        }

        var rows = new Dictionary<int, int>();
        var result = new Dictionary<string, Vector2>();
        foreach (var label in project.Labels.Values)
        {
            var d = depth[label.Name];
            rows.TryGetValue(d, out var row);
            rows[d] = row + 1;
            // Отступ сверху и слева, чтобы узлы не прятались под панелью масштаба графа.
            result[label.Name] = new Vector2(40 + d * ColumnStep, 90 + row * RowStep);
        }
        return result;
    }

    // ---------------------------------------------------------------- оформление узлов

    /// <summary>Вид метки по содержимому: определяет цвет заголовка узла.</summary>
    private static string Category(StoryProject project, LabelDef label)
    {
        if (label.Name == project.Meta.Start) return "start";
        foreach (var instr in label.Code)
        {
            switch (instr.Command)
            {
                case EndCommand: return "end";
                case CheckpointCommand: return "checkpoint";
            }
        }
        foreach (var instr in label.Code)
        {
            if (instr.Op == OpCode.Menu) return "menu";
            if (instr.Command is OsCommand or InputCommand) return "os";
        }
        return "story";
    }

    private static void ColorTitle(GraphNode node, string category)
    {
        var color = category switch
        {
            "start" => new Color("2e7d32"),
            "end" => new Color("b71c1c"),
            "checkpoint" => new Color("6a1b9a"),
            "menu" => new Color("ad1457"),
            "os" => new Color("00838f"),
            _ => new Color("37474f"),
        };
        var bar = UiKit.Box(color, default, 0, 0, 12, 6);
        bar.CornerRadiusTopLeft = bar.CornerRadiusTopRight = 6;
        node.AddThemeStyleboxOverride("titlebar", bar);
        var selected = UiKit.Box(color.Lightened(0.15f), Palette.EdAccent, 0, 0, 12, 6);
        selected.CornerRadiusTopLeft = selected.CornerRadiusTopRight = 6;
        node.AddThemeStyleboxOverride("titlebar_selected", selected);
    }

    /// <summary>Краткое содержание узла: сколько реплик и первая реплика.</summary>
    private static string Summary(LabelDef label)
    {
        var says = 0;
        string first = null;
        foreach (var instr in label.Code)
        {
            if (instr.Command is not SayCommand s) continue;
            says++;
            first ??= s.Text;
        }
        var text = $"{label.Code.Count} команд, {says} реплик";
        if (first != null)
        {
            var plain = GicEngine.Runtime.TextMarkup.Parse(first, null).Plain.Replace('\n', ' ');
            text += "\n«" + (plain.Length > 70 ? plain.Substring(0, 69) + "…" : plain) + "»";
        }
        return text;
    }

    private static string KindLabel(string kind) => kind switch
    {
        "call" => "call",
        "action" => "клик",
        _ => "jump",
    };

    private static Color EdgeColor(string kind) => kind switch
    {
        "call" => new Color("ffb74d"),
        "action" => new Color("4dd0e1"),
        _ => new Color("e0e0e0"),
    };
}
