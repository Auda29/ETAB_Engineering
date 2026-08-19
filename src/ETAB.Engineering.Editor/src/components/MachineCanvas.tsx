import { useEffect, useMemo, useRef, useState } from "react";
import {
  areaViewForGroup,
  getLayoutGroups,
  groupNameFromAreaView,
  nodeGroup,
  nodeMatchesArea,
  type AreaView,
} from "../areaModel";
import type { EtabNode, EtabProjectDocument, EtabRelation, NodeKind, NodeLayout, RelationKind } from "../model";
import { nodeKindLabels } from "../modelFactory";
import { containsDraggedNodeKind, readDraggedNodeKind } from "../nodeDragDrop";
import {
  getAvailableRelationKinds,
  getRelationDefinition,
  hasConnectableTarget,
} from "../relationRules";

const defaultWidth = 222;
const defaultHeight = 112;
const canvasWidth = 1800;
const canvasHeight = 1100;
const canvasPadding = 120;
const minimumZoom = 0.2;
const maximumZoom = 1.6;
const relationLaneSpacing = 36;
const relationKindOrder: RelationKind[] = ["contains", "commands", "observes", "usesRecipe", "usesLink"];

interface DragState {
  nodeId: string;
  startPointerX: number;
  startPointerY: number;
  startX: number;
  startY: number;
}

interface PanState {
  pointerId: number;
  startPointerX: number;
  startPointerY: number;
  startScrollLeft: number;
  startScrollTop: number;
}

interface Point {
  x: number;
  y: number;
}

interface NodeContextMenuState {
  nodeId: string;
  x: number;
  y: number;
}

interface NodeRenameDraft {
  displayName: string;
  name: string;
  symbolStem: string;
}

interface LayoutBounds {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

interface AreaOverview {
  view: AreaView;
  key: string;
  displayName: string;
  nodes: EtabNode[];
  internalRelations: number;
  crossRelations: number;
}

interface AreaConnectionOverview {
  source: string;
  target: string;
  count: number;
  kinds: Partial<Record<RelationKind, number>>;
}

export function MachineCanvas({
  document,
  selectedNodeId,
  onSelect,
  onMoveNode,
  onAddNode,
  onRenameNode,
  onAddCommand,
  activeAreaView,
  onActiveAreaViewChange,
  onCreateArea,
  onRenameArea,
  onDeleteArea,
  onMoveNodeToArea,
  onAddRelation,
  onUpdateRelation,
  onDeleteRelation,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  onSelect: (id: string) => void;
  onMoveNode: (id: string, x: number, y: number) => void;
  onAddNode: (kind: NodeKind, position: { x: number; y: number }, group?: string) => void;
  onRenameNode: (nodeId: string, displayName: string, name: string, symbolStem: string) => void;
  onAddCommand: (nodeId: string) => void;
  activeAreaView: AreaView;
  onActiveAreaViewChange: (view: AreaView) => void;
  onCreateArea: (displayName: string) => string | undefined;
  onRenameArea: (name: string, displayName: string) => void;
  onDeleteArea: (name: string) => void;
  onMoveNodeToArea: (nodeId: string, group?: string) => void;
  onAddRelation: (sourceId: string, targetId: string, kind: RelationKind, label?: string) => void;
  onUpdateRelation: (relationId: string, kind: RelationKind, label?: string) => void;
  onDeleteRelation: (relationId: string) => void;
}) {
  const [showRelations, setShowRelations] = useState(true);
  const [connectSourceId, setConnectSourceId] = useState<string>();
  const [connectTargetId, setConnectTargetId] = useState<string>();
  const [connectKind, setConnectKind] = useState<RelationKind>();
  const [connectLabel, setConnectLabel] = useState("");
  const [selectedRelationId, setSelectedRelationId] = useState<string>();
  const [editKind, setEditKind] = useState<RelationKind>();
  const [editLabel, setEditLabel] = useState("");
  const [paletteDragOver, setPaletteDragOver] = useState(false);
  const [nodeContextMenu, setNodeContextMenu] = useState<NodeContextMenuState>();
  const [nodeRenameDraft, setNodeRenameDraft] = useState<NodeRenameDraft>();
  const [zoom, setZoom] = useState(1);
  const [creatingArea, setCreatingArea] = useState(false);
  const [newAreaName, setNewAreaName] = useState("");
  const [managingArea, setManagingArea] = useState(false);
  const [areaNameDraft, setAreaNameDraft] = useState("");
  const [spacePressed, setSpacePressed] = useState(false);
  const [panning, setPanning] = useState(false);
  const drag = useRef<DragState | undefined>(undefined);
  const pan = useRef<PanState | undefined>(undefined);
  const spacePressedRef = useRef(false);
  const paletteDragDepth = useRef(0);
  const shellRef = useRef<HTMLElement | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);

  const layouts = useMemo(() => {
    const map = new Map<string, NodeLayout>();
    document.nodes.forEach((node, index) => {
      map.set(node.id, document.layout.nodes.find((layout) => layout.nodeId === node.id) ?? {
        nodeId: node.id,
        x: 70 + (index % 4) * 270,
        y: 70 + Math.floor(index / 4) * 175,
      });
    });
    return map;
  }, [document]);

  const groups = useMemo(() => getLayoutGroups(document), [document]);
  const activeGroupName = groupNameFromAreaView(activeAreaView);
  const activeGroup = groups.find((group) => group.name.toLowerCase() === activeGroupName?.toLowerCase());
  const unassignedCount = document.nodes.filter((node) => !nodeGroup(document, node.id)).length;
  const visibleNodes = useMemo(
    () => document.nodes.filter((node) => nodeMatchesArea(document, node.id, activeAreaView)),
    [activeAreaView, document],
  );
  const visibleNodeIds = useMemo(() => new Set(visibleNodes.map((node) => node.id)), [visibleNodes]);
  const visibleRelations = useMemo(
    () => document.relations.filter((relation) =>
      visibleNodeIds.has(relation.sourceNodeId) && visibleNodeIds.has(relation.targetNodeId)),
    [document.relations, visibleNodeIds],
  );
  const relationLaneOffsets = useMemo(
    () => calculateRelationLaneOffsets(visibleRelations),
    [visibleRelations],
  );
  const crossAreaRelations = useMemo(
    () => activeAreaView === "all"
      ? []
      : document.relations.filter((relation) =>
        visibleNodeIds.has(relation.sourceNodeId) !== visibleNodeIds.has(relation.targetNodeId)),
    [activeAreaView, document.relations, visibleNodeIds],
  );
  const canvasDimensions = useMemo(() => {
    let width = canvasWidth;
    let height = canvasHeight;
    for (const node of document.nodes) {
      const layout = layouts.get(node.id);
      if (!layout) continue;
      width = Math.max(width, layout.x + (layout.width ?? defaultWidth) + canvasPadding);
      height = Math.max(height, layout.y + (layout.height ?? defaultHeight) + canvasPadding);
    }
    return { width, height };
  }, [document.nodes, layouts]);
  const visibleBounds = useMemo(
    () => calculateLayoutBounds(visibleNodes, layouts),
    [layouts, visibleNodes],
  );
  const overviewAreas = useMemo(
    () => buildAreaOverview(document, groups),
    [document, groups],
  );
  const overviewConnections = useMemo(
    () => buildAreaConnectionOverview(document, groups),
    [document, groups],
  );

  const sourceNode = document.nodes.find((node) => node.id === connectSourceId);
  const targetNode = document.nodes.find((node) => node.id === connectTargetId);
  const connectKinds = connectSourceId && connectTargetId
    ? getAvailableRelationKinds(document, connectSourceId, connectTargetId)
    : [];
  const selectedRelation = document.relations.find((relation) => relation.id === selectedRelationId);
  const contextNode = document.nodes.find((node) => node.id === nodeContextMenu?.nodeId);
  const contextNodeCanStartRelation = contextNode ? hasConnectableTarget(document, contextNode.id) : false;
  const editKinds = selectedRelation
    ? getAvailableRelationKinds(
      document,
      selectedRelation.sourceNodeId,
      selectedRelation.targetNodeId,
      selectedRelation.id,
    )
    : [];

  useEffect(() => {
    const cancel = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      setConnectSourceId(undefined);
      setConnectTargetId(undefined);
      setConnectKind(undefined);
      setSelectedRelationId(undefined);
      setNodeContextMenu(undefined);
      setNodeRenameDraft(undefined);
    };
    window.addEventListener("keydown", cancel);
    return () => window.removeEventListener("keydown", cancel);
  }, []);

  useEffect(() => {
    const setSpace = (pressed: boolean) => {
      spacePressedRef.current = pressed;
      setSpacePressed(pressed);
    };
    const keyDown = (event: KeyboardEvent) => {
      if (event.code !== "Space" || isKeyboardInteractionTarget(event.target)) return;
      event.preventDefault();
      setSpace(true);
    };
    const keyUp = (event: KeyboardEvent) => {
      if (event.code === "Space") setSpace(false);
    };
    const reset = () => {
      setSpace(false);
      pan.current = undefined;
      setPanning(false);
    };
    window.addEventListener("keydown", keyDown);
    window.addEventListener("keyup", keyUp);
    window.addEventListener("blur", reset);
    return () => {
      window.removeEventListener("keydown", keyDown);
      window.removeEventListener("keyup", keyUp);
      window.removeEventListener("blur", reset);
    };
  }, []);

  useEffect(() => {
    const closeContextMenu = (event: PointerEvent) => {
      if (event.target instanceof Element && event.target.closest(".node-context-menu")) return;
      setNodeContextMenu(undefined);
      setNodeRenameDraft(undefined);
    };
    const closeContextMenuUnconditionally = () => {
      setNodeContextMenu(undefined);
      setNodeRenameDraft(undefined);
    };
    window.addEventListener("pointerdown", closeContextMenu);
    window.addEventListener("resize", closeContextMenuUnconditionally);
    window.addEventListener("blur", closeContextMenuUnconditionally);
    return () => {
      window.removeEventListener("pointerdown", closeContextMenu);
      window.removeEventListener("resize", closeContextMenuUnconditionally);
      window.removeEventListener("blur", closeContextMenuUnconditionally);
    };
  }, []);

  useEffect(() => {
    const resetPaletteDrag = () => {
      paletteDragDepth.current = 0;
      setPaletteDragOver(false);
    };
    window.addEventListener("dragend", resetPaletteDrag);
    window.addEventListener("drop", resetPaletteDrag);
    return () => {
      window.removeEventListener("dragend", resetPaletteDrag);
      window.removeEventListener("drop", resetPaletteDrag);
    };
  }, []);

  useEffect(() => {
    if (!selectedRelationId) return;
    const relation = document.relations.find((item) => item.id === selectedRelationId);
    if (!relation) setSelectedRelationId(undefined);
  }, [document.relations, selectedRelationId]);

  useEffect(() => {
    setManagingArea(false);
    setAreaNameDraft(activeGroup?.displayName ?? "");
    setNodeContextMenu(undefined);
    if (activeAreaView === "all") {
      setConnectSourceId(undefined);
      setConnectTargetId(undefined);
      setConnectKind(undefined);
      setSelectedRelationId(undefined);
    }
  }, [activeAreaView, activeGroup?.displayName]);

  const cancelConnect = () => {
    setConnectSourceId(undefined);
    setConnectTargetId(undefined);
    setConnectKind(undefined);
    setConnectLabel("");
  };

  const beginConnect = (nodeId: string) => {
    setSelectedRelationId(undefined);
    setConnectSourceId(nodeId);
    setConnectTargetId(undefined);
    setConnectKind(undefined);
    setConnectLabel("");
    setShowRelations(true);
    onSelect(nodeId);
  };

  const selectTarget = (targetId: string) => {
    if (!connectSourceId) return;
    const kinds = getAvailableRelationKinds(document, connectSourceId, targetId);
    if (kinds.length === 0) return;
    setConnectTargetId(targetId);
    setConnectKind(kinds[0]);
  };

  const openRelationEditor = (relationId: string) => {
    const relation = document.relations.find((item) => item.id === relationId);
    if (!relation) return;
    cancelConnect();
    setSelectedRelationId(relation.id);
    setEditKind(relation.kind);
    setEditLabel(relation.label ?? "");
  };

  const createRelation = () => {
    if (!connectSourceId || !connectTargetId || !connectKind) return;
    onAddRelation(connectSourceId, connectTargetId, connectKind, connectLabel);
    cancelConnect();
  };

  const saveRelation = () => {
    if (!selectedRelation || !editKind) return;
    onUpdateRelation(selectedRelation.id, editKind, editLabel);
    setSelectedRelationId(undefined);
  };

  const changeZoom = (nextZoom: number) => {
    setZoom(Math.min(maximumZoom, Math.max(minimumZoom, Math.round(nextZoom * 10) / 10)));
    setNodeContextMenu(undefined);
  };

  const fitVisibleNodes = () => {
    const viewport = viewportRef.current;
    if (!viewport || !visibleBounds || activeAreaView === "all") return;
    const horizontalPadding = 72;
    const verticalPadding = 64;
    const contentWidth = Math.max(1, visibleBounds.right - visibleBounds.left);
    const contentHeight = Math.max(1, visibleBounds.bottom - visibleBounds.top);
    const availableWidth = Math.max(1, viewport.clientWidth - horizontalPadding * 2);
    const availableHeight = Math.max(1, viewport.clientHeight - verticalPadding * 2);
    const nextZoom = Math.min(
      1,
      maximumZoom,
      Math.max(minimumZoom, Math.min(availableWidth / contentWidth, availableHeight / contentHeight)),
    );
    const roundedZoom = Math.round(nextZoom * 100) / 100;
    setZoom(roundedZoom);
    setNodeContextMenu(undefined);
    window.requestAnimationFrame(() => {
      const scaledWidth = contentWidth * roundedZoom;
      const scaledHeight = contentHeight * roundedZoom;
      viewport.scrollLeft = Math.max(0, visibleBounds.left * roundedZoom - (viewport.clientWidth - scaledWidth) / 2);
      viewport.scrollTop = Math.max(0, visibleBounds.top * roundedZoom - (viewport.clientHeight - scaledHeight) / 2);
    });
  };

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    if (activeAreaView === "all") {
      viewport.scrollTo({ left: 0, top: 0 });
      return;
    }
    const frame = window.requestAnimationFrame(fitVisibleNodes);
    return () => window.cancelAnimationFrame(frame);
    // Fitting is intentional only when the user changes the active area.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeAreaView]);

  const createArea = () => {
    const name = onCreateArea(newAreaName);
    if (!name) return;
    setNewAreaName("");
    setCreatingArea(false);
    onActiveAreaViewChange(areaViewForGroup(name));
  };

  return (
    <main className="canvas-shell" ref={shellRef}>
      <div className="canvas-toolbar">
        <div className="canvas-toolbar__top">
          <div>
            <strong>Machine canvas</strong>
            <span>{paletteDragOver
              ? "Drop the component at the desired position"
              : connectSourceId
                ? "Choose a highlighted target node; switch areas if necessary"
                : activeAreaView === "all"
                  ? "Project overview. Open an area to edit its nodes and relations"
                  : "Drag empty canvas to pan; use middle-drag or Space + drag anywhere"}</span>
          </div>
          <div className="canvas-toolbar__controls">
            {activeAreaView === "all" ? (
              <span className="canvas-overview-counts">{document.nodes.length} nodes · {document.relations.length} relations</span>
            ) : (
              <>
                <label className="canvas-toggle">
                  <input type="checkbox" checked={showRelations} onChange={(event) => setShowRelations(event.target.checked)} />
                  Relations
                </label>
                <div className="canvas-zoom" aria-label="Canvas zoom">
                  <button type="button" title="Zoom out" disabled={zoom <= minimumZoom} onClick={() => changeZoom(zoom - .1)}>−</button>
                  <button type="button" title="Reset canvas zoom" onClick={() => changeZoom(1)}>{Math.round(zoom * 100)}%</button>
                  <button type="button" title="Zoom in" disabled={zoom >= maximumZoom} onClick={() => changeZoom(zoom + .1)}>+</button>
                  <button className="canvas-zoom__fit" type="button" title="Fit visible nodes" onClick={fitVisibleNodes}>Fit</button>
                </div>
              </>
            )}
          </div>
        </div>
        <div className="canvas-area-bar">
          <div className="canvas-area-tabs" role="tablist" aria-label="Machine areas">
            <button className={activeAreaView === "all" ? "active" : ""} type="button" role="tab" onClick={() => onActiveAreaViewChange("all")}>Overview</button>
            {groups.map((group) => {
              const view = areaViewForGroup(group.name);
              return <button className={activeAreaView === view ? "active" : ""} type="button" role="tab" key={group.name} onClick={() => onActiveAreaViewChange(view)}>{group.displayName}</button>;
            })}
            {unassignedCount > 0 && <button className={activeAreaView === "unassigned" ? "active" : ""} type="button" role="tab" onClick={() => onActiveAreaViewChange("unassigned")}>Unassigned <span>{unassignedCount}</span></button>}
          </div>
          <div className="canvas-area-actions">
            {activeGroup && <button className="canvas-area-action" type="button" title="Area settings" onClick={() => setManagingArea((current) => !current)}>•••</button>}
            {creatingArea ? (
              <div className="canvas-area-create">
                <input
                  autoFocus
                  value={newAreaName}
                  placeholder="Area name"
                  aria-label="New area name"
                  onChange={(event) => setNewAreaName(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") createArea();
                    if (event.key === "Escape") {
                      setCreatingArea(false);
                      setNewAreaName("");
                    }
                  }}
                />
                <button type="button" disabled={!newAreaName.trim()} title="Create area" onClick={createArea}>✓</button>
                <button type="button" title="Cancel" onClick={() => {
                  setCreatingArea(false);
                  setNewAreaName("");
                }}>×</button>
              </div>
            ) : <button className="canvas-area-action canvas-area-action--add" type="button" onClick={() => setCreatingArea(true)}>+ Area</button>}
          </div>
        </div>
      </div>

      <div
        className={[
          "canvas-viewport",
          activeAreaView === "all" ? "canvas-viewport--overview" : "",
          paletteDragOver ? "canvas-viewport--drop-target" : "",
          spacePressed ? "canvas-viewport--pan-ready" : "",
          panning ? "canvas-viewport--panning" : "",
        ].filter(Boolean).join(" ")}
        ref={viewportRef}
        data-testid="machine-canvas"
        style={{ backgroundSize: activeAreaView === "all" ? undefined : `${80 * zoom}px ${80 * zoom}px, ${80 * zoom}px ${80 * zoom}px, ${16 * zoom}px ${16 * zoom}px, ${16 * zoom}px ${16 * zoom}px` }}
        onScroll={() => {
          setNodeContextMenu(undefined);
          setNodeRenameDraft(undefined);
        }}
        onWheel={(event) => {
          if (!event.ctrlKey || activeAreaView === "all") return;
          event.preventDefault();
          changeZoom(zoom + (event.deltaY < 0 ? .1 : -.1));
        }}
        onPointerDown={(event) => {
          if (activeAreaView === "all") return;
          const backgroundPan = event.button === 0 && isCanvasBackground(event.target);
          const spacePan = event.button === 0 && spacePressedRef.current;
          const middlePan = event.button === 1;
          if (!backgroundPan && !spacePan && !middlePan) return;

          event.preventDefault();
          event.currentTarget.setPointerCapture(event.pointerId);
          pan.current = {
            pointerId: event.pointerId,
            startPointerX: event.clientX,
            startPointerY: event.clientY,
            startScrollLeft: event.currentTarget.scrollLeft,
            startScrollTop: event.currentTarget.scrollTop,
          };
          setPanning(true);
          setNodeContextMenu(undefined);
          setNodeRenameDraft(undefined);
        }}
        onPointerMove={(event) => {
          if (!pan.current || pan.current.pointerId !== event.pointerId) return;
          event.currentTarget.scrollLeft = pan.current.startScrollLeft - (event.clientX - pan.current.startPointerX);
          event.currentTarget.scrollTop = pan.current.startScrollTop - (event.clientY - pan.current.startPointerY);
        }}
        onPointerUp={(event) => {
          if (!pan.current || pan.current.pointerId !== event.pointerId) return;
          pan.current = undefined;
          setPanning(false);
        }}
        onPointerCancel={(event) => {
          if (!pan.current || pan.current.pointerId !== event.pointerId) return;
          pan.current = undefined;
          setPanning(false);
        }}
        onLostPointerCapture={(event) => {
          if (!pan.current || pan.current.pointerId !== event.pointerId) return;
          pan.current = undefined;
          setPanning(false);
        }}
        onDragEnter={(event) => {
          if (activeAreaView === "all" || !containsDraggedNodeKind(event.dataTransfer)) return;
          event.preventDefault();
          paletteDragDepth.current += 1;
          setPaletteDragOver(true);
        }}
        onDragOver={(event) => {
          if (activeAreaView === "all" || !containsDraggedNodeKind(event.dataTransfer)) return;
          event.preventDefault();
          event.dataTransfer.dropEffect = "copy";
        }}
        onDragLeave={(event) => {
          if (activeAreaView === "all" || !containsDraggedNodeKind(event.dataTransfer)) return;
          paletteDragDepth.current = Math.max(0, paletteDragDepth.current - 1);
          if (paletteDragDepth.current === 0) setPaletteDragOver(false);
        }}
        onDrop={(event) => {
          if (activeAreaView === "all") return;
          const kind = readDraggedNodeKind(event.dataTransfer);
          if (!kind) return;
          event.preventDefault();
          paletteDragDepth.current = 0;
          setPaletteDragOver(false);

          const viewport = event.currentTarget;
          const bounds = viewport.getBoundingClientRect();
          const rawX = (event.clientX - bounds.left + viewport.scrollLeft) / zoom - defaultWidth / 2;
          const rawY = (event.clientY - bounds.top + viewport.scrollTop) / zoom - defaultHeight / 2;
          const x = Math.min(canvasDimensions.width - defaultWidth - 12, Math.max(12, Math.round(rawX / 4) * 4));
          const y = Math.min(canvasDimensions.height - defaultHeight - 12, Math.max(12, Math.round(rawY / 4) * 4));
          onAddNode(kind, { x, y }, activeGroupName);
        }}
      >
        {activeAreaView === "all" ? (
          <CanvasOverview
            document={document}
            areas={overviewAreas}
            connections={overviewConnections}
            onOpenArea={onActiveAreaViewChange}
          />
        ) : (
        <div className="canvas-stage" style={{ width: canvasDimensions.width * zoom, height: canvasDimensions.height * zoom }}>
        <div className="canvas-world" style={{ width: canvasDimensions.width, height: canvasDimensions.height, transform: `scale(${zoom})` }}>
          {showRelations && (
            <svg className="relation-layer" width={canvasDimensions.width} height={canvasDimensions.height} aria-label="Node relations">
              <defs>
                {(["contains", "commands", "observes", "usesRecipe", "usesLink"] as RelationKind[]).map((kind) => (
                  <marker
                    id={`arrow-${kind}`}
                    key={kind}
                    markerWidth="8"
                    markerHeight="8"
                    refX="7"
                    refY="4"
                    orient="auto"
                    markerUnits="strokeWidth"
                  >
                    <path d="M0,0 L8,4 L0,8 Z" className={`relation-arrow relation-arrow--${kind}`} />
                  </marker>
                ))}
              </defs>
              {visibleRelations.map((relation) => {
                const source = layouts.get(relation.sourceNodeId);
                const target = layouts.get(relation.targetNodeId);
                if (!source || !target) return null;
                const points = connectionPoints(source, target);
                const geometry = relationGeometry(points, relationLaneOffsets.get(relation.id) ?? 0);
                const definition = getRelationDefinition(relation.kind);
                return (
                  <g
                    key={relation.id}
                    className={`relation relation--${relation.kind} ${selectedRelationId === relation.id ? "relation--selected" : ""}`}
                  >
                    <path className="relation-line" d={geometry.path} markerEnd={`url(#arrow-${relation.kind})`} />
                    <path
                      className="relation-hit"
                      d={geometry.path}
                      onClick={() => openRelationEditor(relation.id)}
                      aria-label={`Edit ${definition.label} relation`}
                    />
                    <text
                      x={geometry.label.x}
                      y={geometry.label.y}
                      onClick={() => openRelationEditor(relation.id)}
                    >
                      {relation.label || definition.label}
                    </text>
                  </g>
                );
              })}
            </svg>
          )}

          {visibleNodes.map((node) => {
            const layout = layouts.get(node.id)!;
            const selected = selectedNodeId === node.id;
            const source = connectSourceId === node.id;
            const targetKinds = connectSourceId
              ? getAvailableRelationKinds(document, connectSourceId, node.id)
              : [];
            const validTarget = targetKinds.length > 0;
            const connectDisabled = Boolean(connectSourceId && !source && !validTarget);
            const canStartRelation = hasConnectableTarget(document, node.id);

            return (
              <article
                data-testid={`canvas-node-${node.name}`}
                key={node.id}
                className={[
                  "canvas-node",
                  `canvas-node--${node.kind}`,
                  selected ? "canvas-node--selected" : "",
                  source ? "canvas-node--connect-source" : "",
                  validTarget ? "canvas-node--connect-target" : "",
                  connectDisabled ? "canvas-node--connect-disabled" : "",
                ].filter(Boolean).join(" ")}
                style={{
                  left: layout.x,
                  top: layout.y,
                  width: layout.width ?? defaultWidth,
                  minHeight: layout.height ?? defaultHeight,
                }}
                onContextMenu={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  const bounds = shellRef.current?.getBoundingClientRect();
                  if (!bounds) return;
                  const menuWidth = 246;
                  const menuHeight = 282;
                  setNodeRenameDraft(undefined);
                  setNodeContextMenu({
                    nodeId: node.id,
                    x: Math.max(8, Math.min(event.clientX - bounds.left, bounds.width - menuWidth - 8)),
                    y: Math.max(50, Math.min(event.clientY - bounds.top, bounds.height - menuHeight - 8)),
                  });
                  onSelect(node.id);
                }}
                onPointerDown={(event) => {
                  if (event.button !== 0 || spacePressedRef.current) return;
                  if (connectSourceId) {
                    if (validTarget) selectTarget(node.id);
                    return;
                  }
                  event.currentTarget.setPointerCapture(event.pointerId);
                  drag.current = {
                    nodeId: node.id,
                    startPointerX: event.clientX,
                    startPointerY: event.clientY,
                    startX: layout.x,
                    startY: layout.y,
                  };
                  onSelect(node.id);
                }}
                onPointerMove={(event) => {
                  if (!drag.current || drag.current.nodeId !== node.id) return;
                  const rawX = drag.current.startX + (event.clientX - drag.current.startPointerX) / zoom;
                  const rawY = drag.current.startY + (event.clientY - drag.current.startPointerY) / zoom;
                  const x = Math.min(canvasDimensions.width - defaultWidth - 12, Math.max(12, Math.round(rawX / 4) * 4));
                  const y = Math.min(canvasDimensions.height - defaultHeight - 12, Math.max(12, Math.round(rawY / 4) * 4));
                  onMoveNode(node.id, x, y);
                }}
                onPointerUp={() => { drag.current = undefined; }}
                onPointerCancel={() => { drag.current = undefined; }}
              >
                <div className="canvas-node__accent" />
                <div className="canvas-node__header">
                  <span className="canvas-node__kind">{nodeKindLabels[node.kind]}</span>
                  <span className="canvas-node__role">{node.role}</span>
                </div>
                <strong>{node.displayName}</strong>
                <code>{node.name}</code>
                <div className="canvas-node__stats">
                  <span>{node.commands.length} cmd</span>
                  <span>{node.requestPayload.length} req</span>
                  <span>{node.statusPayload.length} status</span>
                </div>
                {!connectSourceId && (
                  <button
                    type="button"
                    className="canvas-node__connect"
                    disabled={!canStartRelation}
                    title={canStartRelation ? "Create a relation from this node" : "No valid relation target is available"}
                    onPointerDown={(event) => event.stopPropagation()}
                    onClick={(event) => {
                      event.stopPropagation();
                      beginConnect(node.id);
                    }}
                  >
                    {canStartRelation ? "Connect" : "Target only"}
                  </button>
                )}
                {validTarget && <span className="canvas-node__target-hint">Select target</span>}
              </article>
            );
          })}

          {visibleNodes.length === 0 && (
            <div className="canvas-empty">
              <span>↓</span>
              <strong>This area is empty</strong>
              <p>Drop a component here or move an existing node into this area.</p>
            </div>
          )}
        </div>
        </div>
        )}
      </div>

      {activeAreaView !== "all" && paletteDragOver && <div className="canvas-drop-hint">Drop to place component</div>}

      {activeGroup && managingArea && (
        <div className="area-manager-popover">
          <div>
            <span className="eyebrow">Area settings</span>
            <strong>{activeGroup.name}</strong>
          </div>
          <label className="field">
            <span className="field__label">Display name</span>
            <input className="input" autoFocus value={areaNameDraft} onChange={(event) => setAreaNameDraft(event.target.value)} />
          </label>
          <div className="relationship-popover__actions">
            <button className="button button--danger button--compact" type="button" onClick={() => {
              setManagingArea(false);
              onDeleteArea(activeGroup.name);
            }}>Remove area</button>
            <button className="button button--secondary button--compact" type="button" onClick={() => setManagingArea(false)}>Cancel</button>
            <button className="button button--primary button--compact" type="button" disabled={!areaNameDraft.trim()} onClick={() => {
              onRenameArea(activeGroup.name, areaNameDraft);
              setManagingArea(false);
            }}>Save</button>
          </div>
        </div>
      )}

      {showRelations && crossAreaRelations.length > 0 && (
        <div className="cross-area-relations" aria-label="Cross-area relations">
          <span className="cross-area-relations__title">Cross-area</span>
          {crossAreaRelations.slice(0, 5).map((relation) => {
            const sourceVisible = visibleNodeIds.has(relation.sourceNodeId);
            const localNode = document.nodes.find((node) => node.id === (sourceVisible ? relation.sourceNodeId : relation.targetNodeId));
            const remoteNode = document.nodes.find((node) => node.id === (sourceVisible ? relation.targetNodeId : relation.sourceNodeId));
            if (!localNode || !remoteNode) return null;
            const remoteGroup = nodeGroup(document, remoteNode.id);
            const remoteView: AreaView = remoteGroup ? areaViewForGroup(remoteGroup) : "unassigned";
            const remoteArea = groups.find((group) => group.name.toLowerCase() === remoteGroup?.toLowerCase())?.displayName ?? "Unassigned";
            return (
              <button type="button" key={relation.id} title={`Open ${remoteArea}`} onClick={() => {
                onActiveAreaViewChange(remoteView);
                onSelect(remoteNode.id);
              }}>
                <i className={`relation-legend__line relation-legend__line--${relation.kind}`} />
                <span><strong>{localNode.displayName}</strong> {sourceVisible ? "→" : "←"} {remoteNode.displayName}<small>{getRelationDefinition(relation.kind).label} · {remoteArea}</small></span>
              </button>
            );
          })}
          {crossAreaRelations.length > 5 && <small>+{crossAreaRelations.length - 5} more</small>}
        </div>
      )}

      {contextNode && nodeContextMenu && (
        <div
          className="node-context-menu"
          style={{ left: nodeContextMenu.x, top: nodeContextMenu.y }}
          role={nodeRenameDraft ? "dialog" : "menu"}
          aria-label={nodeRenameDraft ? `Rename ${contextNode.displayName}` : `${contextNode.displayName} actions`}
          onContextMenu={(event) => event.preventDefault()}
        >
          <div className="node-context-menu__header">
            <strong>{contextNode.displayName}</strong>
            <span>{nodeKindLabels[contextNode.kind]}</span>
          </div>
          {nodeRenameDraft ? (
            <form className="node-rename-form" onSubmit={(event) => {
              event.preventDefault();
              const displayName = nodeRenameDraft.displayName.trim();
              const name = nodeRenameDraft.name.trim();
              const symbolStem = nodeRenameDraft.symbolStem.trim();
              if (!displayName || !name || !symbolStem) return;
              onRenameNode(contextNode.id, displayName, name, symbolStem);
              setNodeRenameDraft(undefined);
              setNodeContextMenu(undefined);
            }}>
              <label>
                <span>Display name</span>
                <input
                  autoFocus
                  value={nodeRenameDraft.displayName}
                  onChange={(event) => setNodeRenameDraft({ ...nodeRenameDraft, displayName: event.target.value })}
                />
                <small>Shown on the canvas and in the project tree.</small>
              </label>
              <label>
                <span>PLC name</span>
                <input
                  value={nodeRenameDraft.name}
                  onChange={(event) => setNodeRenameDraft({ ...nodeRenameDraft, name: event.target.value })}
                />
                <small>Used for generated instance names.</small>
              </label>
              <label>
                <span>Symbol stem</span>
                <input
                  value={nodeRenameDraft.symbolStem}
                  onChange={(event) => setNodeRenameDraft({ ...nodeRenameDraft, symbolStem: event.target.value })}
                />
                <small>Used in generated DUT and FB names.</small>
              </label>
              <div className="node-rename-form__actions">
                <button type="button" onClick={() => setNodeRenameDraft(undefined)}>Cancel</button>
                <button
                  type="submit"
                  disabled={!nodeRenameDraft.displayName.trim() || !nodeRenameDraft.name.trim() || !nodeRenameDraft.symbolStem.trim()}
                >Save</button>
              </div>
            </form>
          ) : (
            <>
              <button
                type="button"
                role="menuitem"
                onClick={() => setNodeRenameDraft({
                  displayName: contextNode.displayName,
                  name: contextNode.name,
                  symbolStem: contextNode.symbolStem,
                })}
              >
                <span className="node-context-menu__icon node-context-menu__icon--rename">✎</span>
                <span><strong>Rename node</strong><small>Edit display and generated PLC names</small></span>
              </button>
              <button
                type="button"
                role="menuitem"
                disabled={!contextNodeCanStartRelation}
                title={contextNodeCanStartRelation ? "Choose a valid target and relationship type" : "No valid relationship target is available"}
                onClick={() => {
                  setNodeContextMenu(undefined);
                  beginConnect(contextNode.id);
                }}
              >
                <span className="node-context-menu__icon node-context-menu__icon--relation">↗</span>
                <span><strong>Create relationship</strong><small>Select a valid target node</small></span>
              </button>
              <button
                type="button"
                role="menuitem"
                disabled={!contextNode.generate.commandEnum}
                title={contextNode.generate.commandEnum ? "Add a command and open the command editor" : "Command generation is disabled for this node type"}
                onClick={() => {
                  setNodeContextMenu(undefined);
                  onAddCommand(contextNode.id);
                }}
              >
                <span className="node-context-menu__icon node-context-menu__icon--command">+</span>
                <span><strong>Add command</strong><small>{contextNode.generate.commandEnum ? "Open it in the inspector" : "Unavailable for this node type"}</small></span>
              </button>
              <label className="node-context-menu__area">
                <span>Move to area</span>
                <select
                  value={nodeGroup(document, contextNode.id) ?? ""}
                  onChange={(event) => {
                    setNodeContextMenu(undefined);
                    onMoveNodeToArea(contextNode.id, event.target.value || undefined);
                  }}
                >
                  <option value="">Unassigned</option>
                  {groups.map((group) => <option value={group.name} key={group.name}>{group.displayName}</option>)}
                </select>
              </label>
            </>
          )}
        </div>
      )}

      {activeAreaView !== "all" && showRelations && (
        <div className="relation-legend" aria-label="Relation legend">
          {(["contains", "commands", "observes", "usesRecipe", "usesLink"] as RelationKind[]).map((kind) => (
            <span key={kind}><i className={`relation-legend__line relation-legend__line--${kind}`} />{getRelationDefinition(kind).label}</span>
          ))}
        </div>
      )}

      {connectSourceId && !connectTargetId && (
        <div className="relationship-popover relationship-popover--compact">
          <div>
            <span className="eyebrow">Create relation</span>
            <strong>{sourceNode?.displayName}</strong>
            <p>Select one of the highlighted nodes as the target. Invalid targets are dimmed.</p>
          </div>
          <button className="icon-button" type="button" title="Cancel" onClick={cancelConnect}>×</button>
        </div>
      )}

      {connectSourceId && connectTargetId && (
        <div className="relationship-popover">
          <div className="relationship-popover__header">
            <div>
              <span className="eyebrow">Create relation</span>
              <strong>{sourceNode?.displayName} → {targetNode?.displayName}</strong>
            </div>
            <button className="icon-button" type="button" title="Cancel" onClick={cancelConnect}>×</button>
          </div>
          <label className="field">
            <span className="field__label">Relationship type</span>
            <select
              className="input select"
              value={connectKind}
              onChange={(event) => setConnectKind(event.target.value as RelationKind)}
            >
              {connectKinds.map((kind) => (
                <option key={kind} value={kind}>{getRelationDefinition(kind).label} ({kind})</option>
              ))}
            </select>
            {connectKind && <small className="field__hint">{getRelationDefinition(connectKind).description}</small>}
          </label>
          <label className="field">
            <span className="field__label">Optional line label</span>
            <input
              className="input"
              value={connectLabel}
              placeholder={connectKind ? getRelationDefinition(connectKind).label : ""}
              onChange={(event) => setConnectLabel(event.target.value)}
            />
          </label>
          <div className="relationship-popover__actions">
            <button className="button button--secondary button--compact" type="button" onClick={() => {
              setConnectTargetId(undefined);
              setConnectKind(undefined);
            }}>Back</button>
            <button className="button button--primary button--compact" type="button" disabled={!connectKind} onClick={createRelation}>Create relation</button>
          </div>
        </div>
      )}

      {selectedRelation && (
        <div className="relationship-popover">
          <div className="relationship-popover__header">
            <div>
              <span className="eyebrow">Edit relation</span>
              <strong>
                {document.nodes.find((node) => node.id === selectedRelation.sourceNodeId)?.displayName}
                {" → "}
                {document.nodes.find((node) => node.id === selectedRelation.targetNodeId)?.displayName}
              </strong>
            </div>
            <button className="icon-button" type="button" title="Close" onClick={() => setSelectedRelationId(undefined)}>×</button>
          </div>
          <label className="field">
            <span className="field__label">Relationship type</span>
            <select className="input select" value={editKind} onChange={(event) => setEditKind(event.target.value as RelationKind)}>
              {editKinds.map((kind) => (
                <option key={kind} value={kind}>{getRelationDefinition(kind).label} ({kind})</option>
              ))}
            </select>
            {editKind && <small className="field__hint">{getRelationDefinition(editKind).description}</small>}
          </label>
          <label className="field">
            <span className="field__label">Optional line label</span>
            <input className="input" value={editLabel} onChange={(event) => setEditLabel(event.target.value)} />
          </label>
          <div className="relationship-popover__actions">
            <button
              className="button button--danger button--compact"
              type="button"
              onClick={() => {
                onDeleteRelation(selectedRelation.id);
                setSelectedRelationId(undefined);
              }}
            >Delete</button>
            <button className="button button--primary button--compact" type="button" disabled={!editKind} onClick={saveRelation}>Save changes</button>
          </div>
        </div>
      )}
    </main>
  );
}

function CanvasOverview({
  document,
  areas,
  connections,
  onOpenArea,
}: {
  document: EtabProjectDocument;
  areas: AreaOverview[];
  connections: AreaConnectionOverview[];
  onOpenArea: (view: AreaView) => void;
}) {
  const visibleConnections = connections.slice(0, 12);

  return (
    <section className="canvas-overview" aria-label="Project overview">
      <header className="canvas-overview__summary">
        <div>
          <span className="eyebrow">Project map</span>
          <h2>{document.project.displayName}</h2>
          <p>Open an area to edit its nodes and inspect the complete relation graph.</p>
        </div>
        <dl>
          <div><dt>Areas</dt><dd>{areas.length}</dd></div>
          <div><dt>Nodes</dt><dd>{document.nodes.length}</dd></div>
          <div><dt>Relations</dt><dd>{document.relations.length}</dd></div>
        </dl>
      </header>

      <div className="canvas-overview__content">
        <section className="canvas-overview__areas-section" aria-labelledby="overview-areas-title">
          <div className="canvas-overview__section-title">
            <div>
              <h3 id="overview-areas-title">Machine areas</h3>
              <p>Each card is a focused editing view.</p>
            </div>
          </div>
          <div className="canvas-overview__areas">
            {areas.map((area) => {
              const nodeKinds = (["applicationUnit", "commandUnit", "recipeManager", "machineLink"] as NodeKind[])
                .map((kind) => ({
                  kind,
                  count: area.nodes.filter((node) => node.kind === kind).length,
                }))
                .filter((entry) => entry.count > 0);
              const previewNodes = area.nodes.slice(0, 4);
              const hiddenNodeCount = Math.max(0, area.nodes.length - previewNodes.length);

              return (
                <button
                  className="canvas-area-card"
                  type="button"
                  key={area.key}
                  onClick={() => onOpenArea(area.view)}
                  aria-label={`Open ${area.displayName} area`}
                >
                  <span className="canvas-area-card__header">
                    <span>
                      <small>Area</small>
                      <strong>{area.displayName}</strong>
                    </span>
                    <b>{area.nodes.length}</b>
                  </span>
                  <span className="canvas-area-card__kinds">
                    {nodeKinds.length > 0
                      ? nodeKinds.map(({ kind, count }) => (
                        <span key={kind}><i className={`kind-dot kind-dot--${kind}`} />{count} {nodeKindLabels[kind]}</span>
                      ))
                      : <em>Empty area</em>}
                  </span>
                  <span className="canvas-area-card__nodes">
                    {previewNodes.map((node) => <span key={node.id}>{node.displayName}</span>)}
                    {hiddenNodeCount > 0 && <span>+{hiddenNodeCount} more</span>}
                  </span>
                  <span className="canvas-area-card__stats">
                    <span>{area.internalRelations} inside</span>
                    <span>{area.crossRelations} cross-area</span>
                    <strong>Open area <b aria-hidden="true">→</b></strong>
                  </span>
                </button>
              );
            })}
          </div>
        </section>

        <aside className="canvas-overview__connections" aria-labelledby="overview-connections-title">
          <div className="canvas-overview__section-title">
            <div>
              <h3 id="overview-connections-title">Between areas</h3>
              <p>Aggregated cross-area relations.</p>
            </div>
            <span>{connections.length}</span>
          </div>
          <div className="canvas-overview__connection-list">
            {visibleConnections.map((connection) => (
              <div className="canvas-area-connection" key={`${connection.source}\u0000${connection.target}`}>
                <div>
                  <strong>{connection.source}</strong>
                  <span aria-hidden="true">→</span>
                  <strong>{connection.target}</strong>
                  <b>{connection.count}</b>
                </div>
                <p>
                  {relationKindOrder
                    .filter((kind) => connection.kinds[kind])
                    .map((kind) => (
                      <span key={kind}>
                        <i className={`relation-legend__line relation-legend__line--${kind}`} />
                        {getRelationDefinition(kind).label} {connection.kinds[kind]}
                      </span>
                    ))}
                </p>
              </div>
            ))}
            {connections.length === 0 && <div className="canvas-overview__no-connections">No cross-area relations yet.</div>}
            {connections.length > visibleConnections.length && (
              <small className="canvas-overview__more">+{connections.length - visibleConnections.length} more area connections</small>
            )}
          </div>
        </aside>
      </div>
    </section>
  );
}

function calculateLayoutBounds(nodes: EtabNode[], layouts: Map<string, NodeLayout>): LayoutBounds | undefined {
  if (nodes.length === 0) return undefined;

  let left = Number.POSITIVE_INFINITY;
  let top = Number.POSITIVE_INFINITY;
  let right = Number.NEGATIVE_INFINITY;
  let bottom = Number.NEGATIVE_INFINITY;

  for (const node of nodes) {
    const layout = layouts.get(node.id);
    if (!layout) continue;
    left = Math.min(left, layout.x);
    top = Math.min(top, layout.y);
    right = Math.max(right, layout.x + (layout.width ?? defaultWidth));
    bottom = Math.max(bottom, layout.y + (layout.height ?? defaultHeight));
  }

  return Number.isFinite(left) ? { left, top, right, bottom } : undefined;
}

function buildAreaOverview(document: EtabProjectDocument, groups: ReturnType<typeof getLayoutGroups>): AreaOverview[] {
  const areaDefinitions = groups.map((group) => ({
    view: areaViewForGroup(group.name),
    key: group.name.toLowerCase(),
    displayName: group.displayName,
    groupName: group.name,
  }));
  const unassignedNodes = document.nodes.filter((node) => !nodeGroup(document, node.id));
  if (unassignedNodes.length > 0) {
    areaDefinitions.push({
      view: "unassigned",
      key: "__unassigned__",
      displayName: "Unassigned",
      groupName: "",
    });
  }

  return areaDefinitions.map((area) => {
    const nodes = area.view === "unassigned"
      ? unassignedNodes
      : document.nodes.filter((node) => nodeGroup(document, node.id)?.toLowerCase() === area.groupName.toLowerCase());
    const nodeIds = new Set(nodes.map((node) => node.id));
    let internalRelations = 0;
    let crossRelations = 0;

    for (const relation of document.relations) {
      const sourceInside = nodeIds.has(relation.sourceNodeId);
      const targetInside = nodeIds.has(relation.targetNodeId);
      if (sourceInside && targetInside) internalRelations += 1;
      else if (sourceInside !== targetInside) crossRelations += 1;
    }

    return {
      view: area.view,
      key: area.key,
      displayName: area.displayName,
      nodes,
      internalRelations,
      crossRelations,
    };
  });
}

function buildAreaConnectionOverview(
  document: EtabProjectDocument,
  groups: ReturnType<typeof getLayoutGroups>,
): AreaConnectionOverview[] {
  const groupNames = new Map(groups.map((group) => [group.name.toLowerCase(), group.displayName]));
  const nodeAreas = new Map(document.nodes.map((node) => {
    const group = nodeGroup(document, node.id);
    return [node.id, group
      ? { key: group.toLowerCase(), displayName: groupNames.get(group.toLowerCase()) ?? group }
      : { key: "__unassigned__", displayName: "Unassigned" }] as const;
  }));
  const connections = new Map<string, AreaConnectionOverview>();

  for (const relation of document.relations) {
    const sourceArea = nodeAreas.get(relation.sourceNodeId);
    const targetArea = nodeAreas.get(relation.targetNodeId);
    if (!sourceArea || !targetArea || sourceArea.key === targetArea.key) continue;
    const key = `${sourceArea.key}\u0000${targetArea.key}`;
    const connection = connections.get(key) ?? {
      source: sourceArea.displayName,
      target: targetArea.displayName,
      count: 0,
      kinds: {},
    };
    connection.count += 1;
    connection.kinds[relation.kind] = (connection.kinds[relation.kind] ?? 0) + 1;
    connections.set(key, connection);
  }

  return [...connections.values()].sort((left, right) =>
    right.count - left.count
    || left.source.localeCompare(right.source)
    || left.target.localeCompare(right.target));
}

function connectionPoints(source: NodeLayout, target: NodeLayout): { source: Point; target: Point } {
  const sourceWidth = source.width ?? defaultWidth;
  const sourceHeight = source.height ?? defaultHeight;
  const targetWidth = target.width ?? defaultWidth;
  const targetHeight = target.height ?? defaultHeight;
  const sourceCenter = { x: source.x + sourceWidth / 2, y: source.y + sourceHeight / 2 };
  const targetCenter = { x: target.x + targetWidth / 2, y: target.y + targetHeight / 2 };
  const horizontal = Math.abs(targetCenter.x - sourceCenter.x) >= Math.abs(targetCenter.y - sourceCenter.y);

  if (horizontal) {
    const leftToRight = targetCenter.x >= sourceCenter.x;
    return {
      source: { x: leftToRight ? source.x + sourceWidth : source.x, y: sourceCenter.y },
      target: { x: leftToRight ? target.x : target.x + targetWidth, y: targetCenter.y },
    };
  }

  const topToBottom = targetCenter.y >= sourceCenter.y;
  return {
    source: { x: sourceCenter.x, y: topToBottom ? source.y + sourceHeight : source.y },
    target: { x: targetCenter.x, y: topToBottom ? target.y : target.y + targetHeight },
  };
}

function calculateRelationLaneOffsets(relations: EtabRelation[]): Map<string, number> {
  const relationGroups = new Map<string, EtabRelation[]>();

  relations.forEach((relation) => {
    const key = relationPairKey(relation.sourceNodeId, relation.targetNodeId);
    const group = relationGroups.get(key) ?? [];
    group.push(relation);
    relationGroups.set(key, group);
  });

  const offsets = new Map<string, number>();
  relationGroups.forEach((group) => {
    group
      .sort((left, right) => {
        const kindDifference = relationKindOrder.indexOf(left.kind) - relationKindOrder.indexOf(right.kind);
        return kindDifference || left.id.localeCompare(right.id);
      })
      .forEach((relation, index) => {
        const centeredIndex = index - (group.length - 1) / 2;
        const canonicalDirection = relation.sourceNodeId.localeCompare(relation.targetNodeId) <= 0 ? 1 : -1;
        offsets.set(relation.id, centeredIndex * relationLaneSpacing * canonicalDirection);
      });
  });

  return offsets;
}

function relationPairKey(sourceNodeId: string, targetNodeId: string): string {
  return sourceNodeId.localeCompare(targetNodeId) <= 0
    ? `${sourceNodeId}\u0000${targetNodeId}`
    : `${targetNodeId}\u0000${sourceNodeId}`;
}

function relationGeometry(points: { source: Point; target: Point }, laneOffset: number): { path: string; label: Point } {
  const deltaX = points.target.x - points.source.x;
  const deltaY = points.target.y - points.source.y;
  const length = Math.hypot(deltaX, deltaY) || 1;
  const normal = { x: -deltaY / length, y: deltaX / length };
  const horizontal = Math.abs(deltaX) >= Math.abs(deltaY);
  const distance = horizontal ? Math.abs(deltaX) : Math.abs(deltaY);
  const curve = Math.max(42, distance * 0.38);
  const direction = (horizontal ? deltaX : deltaY) >= 0 ? 1 : -1;
  const firstControl = horizontal
    ? { x: points.source.x + curve * direction, y: points.source.y }
    : { x: points.source.x, y: points.source.y + curve * direction };
  const secondControl = horizontal
    ? { x: points.target.x - curve * direction, y: points.target.y }
    : { x: points.target.x, y: points.target.y - curve * direction };

  firstControl.x += normal.x * laneOffset;
  firstControl.y += normal.y * laneOffset;
  secondControl.x += normal.x * laneOffset;
  secondControl.y += normal.y * laneOffset;

  const midpoint = cubicPoint(points.source, firstControl, secondControl, points.target, 0.5);
  return {
    path: `M ${points.source.x} ${points.source.y} C ${firstControl.x} ${firstControl.y}, ${secondControl.x} ${secondControl.y}, ${points.target.x} ${points.target.y}`,
    label: {
      x: midpoint.x + normal.x * laneOffset * 0.15,
      y: midpoint.y + normal.y * laneOffset * 0.15 - 8,
    },
  };
}

function cubicPoint(start: Point, firstControl: Point, secondControl: Point, end: Point, position: number): Point {
  const inverse = 1 - position;
  return {
    x: inverse ** 3 * start.x
      + 3 * inverse ** 2 * position * firstControl.x
      + 3 * inverse * position ** 2 * secondControl.x
      + position ** 3 * end.x,
    y: inverse ** 3 * start.y
      + 3 * inverse ** 2 * position * firstControl.y
      + 3 * inverse * position ** 2 * secondControl.y
      + position ** 3 * end.y,
  };
}

function isCanvasBackground(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return !target.closest(".canvas-node, .relation, button, input, select, textarea, a, [contenteditable='true']");
}

function isKeyboardInteractionTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return Boolean(target.closest("button, input, select, textarea, a, [role='button'], [contenteditable='true']"));
}
