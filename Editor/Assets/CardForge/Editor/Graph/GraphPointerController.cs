using CardForge.DesignLogic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>Turns pointer and wheel input on a graph canvas into pan, zoom, node drags and new connections.</summary>
    internal sealed class GraphPointerController
    {
        private const float SnapSize = 10f;
        private const float PortHitRadius = 14f;
        private const float EdgeHitTolerance = 9f;
        private const float DragThreshold = 3f;
        private const float WheelSensitivity = 0.04f;
        private const float WheelLimit = 12f;
        private const int MiddleButton = 2;

        private enum Mode
        {
            None,
            Panning,
            DraggingNode,
            Connecting
        }

        private readonly GraphCanvasView host;
        private Mode mode;
        private int pointerId;
        private Vector2 lastLocal;
        private Vector2 downLocal;
        private string activeNodeId;
        private Vector2 nodeStart;
        private bool nodeMoved;
        private string dropTargetId;

        public GraphPointerController(GraphCanvasView host)
        {
            this.host = host;
            host.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            host.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            host.RegisterCallback<PointerUpEvent>(OnPointerUp);
            host.RegisterCallback<PointerCaptureOutEvent>(_ => ResetState());
            host.RegisterCallback<WheelEvent>(OnWheel);
        }

        private static Vector2 Local(IPointerEvent evt) => new Vector2(evt.localPosition.x, evt.localPosition.y);

        private float PortRadius => PortHitRadius / host.Transform.Zoom;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (host.IsOverToolbar(evt.target)) return;
            host.Focus();
            Vector2 local = Local(evt);
            Vector2 world = host.ToWorld(local);
            if (evt.button == MiddleButton)
            {
                BeginPan(evt, local);
                return;
            }
            if (evt.button != 0) return;
            string portNodeId = host.HitOutputPort(world, PortRadius);
            if (portNodeId != null)
            {
                BeginConnect(evt, portNodeId, world);
                return;
            }
            string nodeId = host.HitNode(world);
            if (nodeId != null)
            {
                BeginNodeDrag(evt, nodeId, local);
                return;
            }
            int linkIndex = host.HitLink(world, EdgeHitTolerance / host.Transform.Zoom);
            if (linkIndex >= 0)
            {
                host.SelectLink(linkIndex);
                evt.StopPropagation();
                return;
            }
            host.ClearSelection();
            if (evt.clickCount == 2) host.RequestAddNodeAt(world);
            else BeginPan(evt, local);
        }

        private void BeginPan(PointerDownEvent evt, Vector2 local)
        {
            Begin(evt, Mode.Panning);
            lastLocal = local;
        }

        private void BeginNodeDrag(PointerDownEvent evt, string nodeId, Vector2 local)
        {
            host.SelectNode(nodeId, true);
            Begin(evt, Mode.DraggingNode);
            activeNodeId = nodeId;
            downLocal = local;
            nodeStart = host.NodePosition(nodeId);
            nodeMoved = false;
        }

        private void BeginConnect(PointerDownEvent evt, string nodeId, Vector2 world)
        {
            Begin(evt, Mode.Connecting);
            activeNodeId = nodeId;
            host.PreviewConnection(host.OutputPortPosition(nodeId), world);
        }

        private void Begin(PointerDownEvent evt, Mode newMode)
        {
            mode = newMode;
            pointerId = evt.pointerId;
            host.CapturePointer(pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 local = Local(evt);
            if (mode == Mode.None)
            {
                host.HoverOutputPort(host.ToWorld(local), PortRadius);
                return;
            }
            if (evt.pointerId != pointerId) return;
            if (mode == Mode.Panning) Pan(local);
            else if (mode == Mode.DraggingNode) DragNode(local, evt.altKey);
            else UpdateConnection(host.ToWorld(local));
            evt.StopPropagation();
        }

        private void Pan(Vector2 local)
        {
            host.Transform.PanBy(local.x - lastLocal.x, local.y - lastLocal.y);
            lastLocal = local;
            host.ApplyTransform();
        }

        private void DragNode(Vector2 local, bool snapDisabled)
        {
            Vector2 distance = local - downLocal;
            if (!nodeMoved && distance.magnitude < DragThreshold) return;
            nodeMoved = true;
            Vector2 target = nodeStart + distance / host.Transform.Zoom;
            float x = Mathf.Max(0f, snapDisabled ? target.x : Snap(target.x));
            float y = Mathf.Max(0f, snapDisabled ? target.y : Snap(target.y));
            host.MoveNodeTo(activeNodeId, x, y);
        }

        private void UpdateConnection(Vector2 world)
        {
            host.PreviewConnection(host.OutputPortPosition(activeNodeId), world);
            string hovered = host.HitNode(world);
            if (hovered == activeNodeId) hovered = null;
            if (hovered == dropTargetId) return;
            if (dropTargetId != null) host.SetDropState(dropTargetId, GraphDropState.None);
            dropTargetId = hovered;
            if (dropTargetId != null) host.SetDropState(dropTargetId, host.CanConnect(activeNodeId, dropTargetId) ? GraphDropState.Accepts : GraphDropState.Rejects);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (mode == Mode.None || evt.pointerId != pointerId) return;
            Mode finished = mode;
            string nodeId = activeNodeId;
            string targetId = dropTargetId;
            bool moved = nodeMoved;
            if (host.HasPointerCapture(pointerId)) host.ReleasePointer(pointerId);
            ResetState();
            evt.StopPropagation();
            if (finished == Mode.DraggingNode && moved) host.CommitNodeMove(nodeId);
            else if (finished == Mode.Connecting && targetId != null) host.RequestConnection(nodeId, targetId);
        }

        private void ResetState()
        {
            if (dropTargetId != null) host.SetDropState(dropTargetId, GraphDropState.None);
            host.PreviewConnection(null, null);
            mode = Mode.None;
            activeNodeId = null;
            dropTargetId = null;
            nodeMoved = false;
        }

        private void OnWheel(WheelEvent evt)
        {
            float step = Mathf.Clamp(evt.delta.y, -WheelLimit, WheelLimit);
            Vector2 anchor = host.WorldToLocal(evt.mousePosition);
            host.Transform.ZoomAt(Mathf.Exp(-step * WheelSensitivity), anchor.x, anchor.y);
            host.ApplyTransform();
            evt.StopPropagation();
        }

        private static float Snap(float value) => Mathf.Round(value / SnapSize) * SnapSize;
    }
}
