using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.DesignLogic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>
    /// Pannable, zoomable node graph with drag-to-connect. Hosts any graph the editor needs;
    /// owners feed it <see cref="GraphNodeData"/> and react to the events it raises.
    /// </summary>
    public sealed class GraphCanvasView : VisualElement, IGraphGeometry
    {
        private const float FitPadding = 48f;
        private const float ExtentMargin = 900f;
        private const float MinimumExtentWidth = 2400f;
        private const float MinimumExtentHeight = 1600f;
        private const float NudgeSize = 10f;
        private const float LargeNudgeFactor = 5f;
        private const float ZoomStep = 1.2f;
        private const float MinimumUsableViewSize = 10f;

        private readonly GraphViewTransform viewTransform = new GraphViewTransform();
        private readonly VisualElement world = new VisualElement { name = "graph-world" };
        private readonly GraphGridLayer grid;
        private readonly GraphEdgeLayer edges;
        private readonly GraphToolbar toolbar = new GraphToolbar();
        private readonly List<GraphNodeData> nodes = new List<GraphNodeData>();
        private readonly Dictionary<string, GraphNodeView> viewsById = new Dictionary<string, GraphNodeView>();
        private IReadOnlyList<GraphLinkData> links = new List<GraphLinkData>();
        private string selectedNodeId;
        private string hotPortNodeId;
        private bool viewInitialised;
        private bool fitPending;

        public GraphCanvasView()
        {
            AddToClassList("cf-graph");
            focusable = true;
            grid = new GraphGridLayer(viewTransform);
            edges = new GraphEdgeLayer(this);
            world.AddToClassList("cf-graph__world");
            world.pickingMode = PickingMode.Ignore;
            Add(grid);
            Add(world);
            world.Add(edges);
            Add(toolbar);
            Add(Hint());
            WireToolbar();
            new GraphPointerController(this);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(_ => OnViewResized());
            ApplyTransform();
        }

        /// <summary>Raised when the user selects a node. The argument is the node id.</summary>
        public event Action<string> NodeSelected;

        /// <summary>Raised once after one or more nodes were moved by the user.</summary>
        public event Action<IReadOnlyList<GraphNodeMove>> NodesMoved;

        /// <summary>Raised when the user drags a new connection from one node to another.</summary>
        public event Action<string, string> ConnectRequested;

        /// <summary>Raised when the user deletes the selected connection.</summary>
        public event Action<string, string> DisconnectRequested;

        /// <summary>Raised when the user selects a connection.</summary>
        public event Action<string, string> LinkSelected;

        /// <summary>Raised when the user asks for a new node at a world position.</summary>
        public event Action<float, float> AddNodeRequested;

        /// <summary>Key under which pan and zoom are remembered between redraws.</summary>
        public string ViewKey { get; set; }

        /// <summary>Text of the toolbar button that adds a node. Empty hides the button.</summary>
        public string AddNodeLabel
        {
            set => toolbar.AddLabel = value;
        }

        internal GraphViewTransform Transform => viewTransform;

        /// <summary>Replaces the drawn graph. Pan and zoom are kept.</summary>
        public void SetContent(IReadOnlyList<GraphNodeData> newNodes, IReadOnlyList<GraphLinkData> newLinks, string selectedId)
        {
            foreach (var view in viewsById.Values) view.RemoveFromHierarchy();
            viewsById.Clear();
            hotPortNodeId = null;
            nodes.Clear();
            nodes.AddRange(newNodes);
            foreach (var data in nodes) AddNodeView(data);
            links = newLinks ?? new List<GraphLinkData>();
            edges.SetLinks(links);
            selectedNodeId = selectedId != null && viewsById.ContainsKey(selectedId) ? selectedId : null;
            ApplySelection();
            UpdateExtent();
            InitialiseView();
        }

        /// <summary>Scales and centres the view so every node is visible.</summary>
        public void FitToContent()
        {
            if (!HasUsableSize())
            {
                fitPending = true;
                return;
            }
            fitPending = false;
            if (nodes.Count == 0)
            {
                viewTransform.Set(1f, FitPadding, FitPadding);
                ApplyTransform();
                return;
            }
            var bounds = nodes.Select(node => RectOf(node)).Aggregate((first, second) => Union(first, second));
            viewTransform.FitTo(bounds.xMin, bounds.yMin, bounds.width, bounds.height, layout.width, layout.height, FitPadding);
            ApplyTransform();
        }

        /// <inheritdoc />
        public bool TryGetNodeRect(string nodeId, out Rect rect)
        {
            rect = default;
            var data = nodes.FirstOrDefault(node => node.Id == nodeId);
            if (data == null) return false;
            rect = RectOf(data);
            return true;
        }

        internal bool IsOverToolbar(IEventHandler target) => target is VisualElement element && (element == toolbar || toolbar.Contains(element));

        internal Vector2 ToWorld(Vector2 local) => new Vector2(viewTransform.ToWorldX(local.x), viewTransform.ToWorldY(local.y));

        internal Vector2 NodePosition(string id) => new Vector2(Find(id).X, Find(id).Y);

        internal Vector2 OutputPortPosition(string id)
        {
            TryGetNodeRect(id, out Rect rect);
            return new Vector2(rect.xMax, rect.center.y);
        }

        internal string HitNode(Vector2 worldPoint)
        {
            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                if (RectOf(nodes[index]).Contains(worldPoint)) return nodes[index].Id;
            }
            return null;
        }

        internal string HitOutputPort(Vector2 worldPoint, float radius)
        {
            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                if (Vector2.Distance(OutputPortPosition(nodes[index].Id), worldPoint) <= radius) return nodes[index].Id;
            }
            return null;
        }

        internal int HitLink(Vector2 worldPoint, float tolerance) => edges.HitTest(worldPoint, tolerance);

        internal void HoverOutputPort(Vector2 worldPoint, float radius)
        {
            string hovered = HitOutputPort(worldPoint, radius);
            if (hovered == hotPortNodeId) return;
            if (hotPortNodeId != null && viewsById.TryGetValue(hotPortNodeId, out var previous)) previous.SetOutputPortHot(false);
            hotPortNodeId = hovered;
            if (hotPortNodeId != null) viewsById[hotPortNodeId].SetOutputPortHot(true);
        }

        internal void SelectNode(string id, bool notify)
        {
            selectedNodeId = id;
            edges.SelectedLinkIndex = -1;
            ApplySelection();
            if (notify) NodeSelected?.Invoke(id);
        }

        internal void SelectLink(int index)
        {
            selectedNodeId = null;
            edges.SelectedLinkIndex = index;
            ApplySelection();
            LinkSelected?.Invoke(links[index].FromId, links[index].ToId);
        }

        internal void ClearSelection()
        {
            selectedNodeId = null;
            edges.SelectedLinkIndex = -1;
            ApplySelection();
        }

        internal void MoveNodeTo(string id, float x, float y)
        {
            var data = Find(id);
            data.X = x;
            data.Y = y;
            viewsById[id].style.left = x;
            viewsById[id].style.top = y;
            UpdateExtent();
            edges.Refresh();
        }

        internal void CommitNodeMove(string id)
        {
            var data = Find(id);
            NodesMoved?.Invoke(new[] { new GraphNodeMove(id, data.X, data.Y) });
        }

        internal void PreviewConnection(Vector2? start, Vector2? end) => edges.SetPreview(start, end);

        internal void SetDropState(string id, GraphDropState state)
        {
            if (viewsById.TryGetValue(id, out var view)) view.SetDropState(state);
        }

        internal bool CanConnect(string fromId, string toId)
        {
            return GraphConnectionRules.CanConnect(fromId, toId, links.Select(link => new KeyValuePair<string, string>(link.FromId, link.ToId)));
        }

        internal void RequestConnection(string fromId, string toId)
        {
            if (CanConnect(fromId, toId)) ConnectRequested?.Invoke(fromId, toId);
        }

        internal void RequestAddNodeAt(Vector2 worldPoint)
        {
            AddNodeRequested?.Invoke(Snap(worldPoint.x - GraphNodeView.DefaultWidth / 2f), Snap(worldPoint.y - GraphNodeView.DefaultHeight / 2f));
        }

        internal void ApplyTransform()
        {
            world.style.translate = new Translate(viewTransform.PanX, viewTransform.PanY);
            world.style.scale = new Scale(new Vector3(viewTransform.Zoom, viewTransform.Zoom, 1f));
            grid.MarkDirtyRepaint();
            toolbar.ShowZoom(viewTransform.Zoom);
            GraphViewMemory.Save(ViewKey, viewTransform.Zoom, viewTransform.PanX, viewTransform.PanY);
        }

        private static float Snap(float value) => Mathf.Max(0f, Mathf.Round(value / 10f) * 10f);

        private static Rect Union(Rect first, Rect second) => Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin), Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax), Mathf.Max(first.yMax, second.yMax));

        private static VisualElement Hint()
        {
            var hint = new Label("Drag to pan \u00b7 wheel to zoom \u00b7 drag a node's right dot to connect \u00b7 click a line, then Delete") { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("cf-graph__hint");
            return hint;
        }

        private GraphNodeData Find(string id) => nodes.First(node => node.Id == id);

        private Rect RectOf(GraphNodeData data)
        {
            float width = viewsById.TryGetValue(data.Id, out var view) ? view.MeasuredWidth : GraphNodeView.DefaultWidth;
            float height = view != null ? view.MeasuredHeight : GraphNodeView.DefaultHeight;
            return new Rect(data.X, data.Y, width, height);
        }

        private bool HasUsableSize() => !float.IsNaN(layout.width) && layout.width > MinimumUsableViewSize && layout.height > MinimumUsableViewSize;

        private void AddNodeView(GraphNodeData data)
        {
            var view = new GraphNodeView(data);
            view.style.left = data.X;
            view.style.top = data.Y;
            view.RegisterCallback<GeometryChangedEvent>(_ => edges.Refresh());
            viewsById[data.Id] = view;
            world.Add(view);
        }

        private void ApplySelection()
        {
            foreach (var pair in viewsById) pair.Value.SetSelected(pair.Key == selectedNodeId);
            edges.HighlightedNodeId = selectedNodeId;
            edges.Refresh();
        }

        private void UpdateExtent()
        {
            float right = nodes.Count == 0 ? 0f : nodes.Max(node => node.X + GraphNodeView.DefaultWidth);
            float bottom = nodes.Count == 0 ? 0f : nodes.Max(node => node.Y + GraphNodeView.DefaultHeight);
            edges.style.width = Mathf.Max(MinimumExtentWidth, right + ExtentMargin);
            edges.style.height = Mathf.Max(MinimumExtentHeight, bottom + ExtentMargin);
        }

        private void InitialiseView()
        {
            if (viewInitialised) return;
            viewInitialised = true;
            if (GraphViewMemory.TryLoad(ViewKey, out float zoom, out float panX, out float panY))
            {
                viewTransform.Set(zoom, panX, panY);
                ApplyTransform();
                return;
            }
            FitToContent();
        }

        private void OnViewResized()
        {
            grid.MarkDirtyRepaint();
            if (fitPending) FitToContent();
        }

        private void WireToolbar()
        {
            toolbar.ZoomInClicked += () => ZoomAroundCentre(ZoomStep);
            toolbar.ZoomOutClicked += () => ZoomAroundCentre(1f / ZoomStep);
            toolbar.FitClicked += FitToContent;
            toolbar.TidyClicked += Tidy;
            toolbar.AddClicked += () => RequestAddNodeAt(ToWorld(new Vector2(layout.width / 2f, layout.height / 2f)));
        }

        private void ZoomAroundCentre(float factor)
        {
            viewTransform.ZoomAt(factor, layout.width / 2f, layout.height / 2f);
            ApplyTransform();
        }

        private void Tidy()
        {
            if (nodes.Count == 0) return;
            var ids = nodes.Select(node => node.Id).ToList();
            var pairs = links.Select(link => new KeyValuePair<string, string>(link.FromId, link.ToId)).ToList();
            // The Action editor has wider, taller narrative nodes. Tidy must use
            // actual node dimensions or it will overlap explanations and ports.
            var nodeWidth = Mathf.Max(GraphNodeView.DefaultWidth,
                viewsById.Values.Max(view => view.MeasuredWidth));
            var settings = new GraphLayoutSettings
            {
                NodeWidth = nodeWidth,
                NodeHeight = Mathf.Max(GraphNodeView.DefaultHeight,
                    viewsById.Values.Max(view => view.MeasuredHeight)),
                MaximumColumns = nodeWidth > 260f ? 3 : 5
            };
            var moves = new List<GraphNodeMove>();
            foreach (var placement in GraphLayout.Arrange(ids, pairs, settings))
            {
                MoveNodeTo(placement.Id, placement.X, placement.Y);
                moves.Add(new GraphNodeMove(placement.Id, placement.X, placement.Y));
            }
            FitToContent();
            NodesMoved?.Invoke(moves);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelectedLink();
                    break;
                case KeyCode.F:
                    FitToContent();
                    break;
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    viewTransform.ZoomTo(1f, layout.width / 2f, layout.height / 2f);
                    ApplyTransform();
                    break;
                case KeyCode.Escape:
                    ClearSelection();
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    NudgeSelectedNode(evt.keyCode, evt.shiftKey);
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        private void DeleteSelectedLink()
        {
            int index = edges.SelectedLinkIndex;
            if (index < 0 || index >= links.Count) return;
            DisconnectRequested?.Invoke(links[index].FromId, links[index].ToId);
        }

        private void NudgeSelectedNode(KeyCode key, bool large)
        {
            if (selectedNodeId == null) return;
            float distance = NudgeSize * (large ? LargeNudgeFactor : 1f);
            var data = Find(selectedNodeId);
            float x = key == KeyCode.LeftArrow ? data.X - distance : key == KeyCode.RightArrow ? data.X + distance : data.X;
            float y = key == KeyCode.UpArrow ? data.Y - distance : key == KeyCode.DownArrow ? data.Y + distance : data.Y;
            MoveNodeTo(selectedNodeId, Mathf.Max(0f, x), Mathf.Max(0f, y));
            CommitNodeMove(selectedNodeId);
        }
    }
}
