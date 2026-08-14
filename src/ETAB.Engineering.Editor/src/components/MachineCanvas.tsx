import { useEffect, useMemo, useRef, useState } from "react";
import {
  areaViewForGroup,
  getLayoutGroups,
  groupNameFromAreaView,
  nodeGroup,
  nodeMatchesArea,
  type AreaView,
} from "../areaModel";
import type { EtabProjectDocument, NodeKind, NodeLayout, RelationKind } from "../model";
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

interface DragState {
  nodeId: string;
  startPointerX: number;
  startPointerY: number;
  startX: number;
  startY: number;
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
  const drag = useRef<DragState | undefined>(undefined);
  const paletteDragDepth = useRef(0);
  const shellRef = useRef<HTMLElement | null>(null);

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
  const crossAreaRelations = useMemo(
    () => activeAreaView === "all"
      ? []
      : document.relations.filter((relation) =>
        visibleNodeIds.has(relation.sourceNodeId) !== visibleNodeIds.has(relation.targetNodeId)),
    [activeAreaView, document.relations, visibleNodeIds],
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
    setZoom(Math.min(1.6, Math.max(0.5, Math.round(nextZoom * 10) / 10)));
    setNodeContextMenu(undefined);
  };

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
                : "Drag components onto the canvas; right-click nodes for actions"}</span>
          </div>
          <div className="canvas-toolbar__controls">
            <label className="canvas-toggle">
              <input type="checkbox" checked={showRelations} onChange={(event) => setShowRelations(event.target.checked)} />
              Relations
            </label>
            <div className="canvas-zoom" aria-label="Canvas zoom">
              <button type="button" title="Zoom out" onClick={() => changeZoom(zoom - .1)}>−</button>
              <button type="button" title="Reset canvas zoom" onClick={() => changeZoom(1)}>{Math.round(zoom * 100)}%</button>
              <button type="button" title="Zoom in" onClick={() => changeZoom(zoom + .1)}>+</button>
            </div>
          </div>
        </div>
        <div className="canvas-area-bar">
          <div className="canvas-area-tabs" role="tablist" aria-label="Machine areas">
            <button className={activeAreaView === "all" ? "active" : ""} type="button" role="tab" onClick={() => onActiveAreaViewChange("all")}>All</button>
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
        className={`canvas-viewport ${paletteDragOver ? "canvas-viewport--drop-target" : ""}`}
        data-testid="machine-canvas"
        style={{ backgroundSize: `${80 * zoom}px ${80 * zoom}px, ${80 * zoom}px ${80 * zoom}px, ${16 * zoom}px ${16 * zoom}px, ${16 * zoom}px ${16 * zoom}px` }}
        onScroll={() => {
          setNodeContextMenu(undefined);
          setNodeRenameDraft(undefined);
        }}
        onWheel={(event) => {
          if (!event.ctrlKey) return;
          event.preventDefault();
          changeZoom(zoom + (event.deltaY < 0 ? .1 : -.1));
        }}
        onDragEnter={(event) => {
          if (!containsDraggedNodeKind(event.dataTransfer)) return;
          event.preventDefault();
          paletteDragDepth.current += 1;
          setPaletteDragOver(true);
        }}
        onDragOver={(event) => {
          if (!containsDraggedNodeKind(event.dataTransfer)) return;
          event.preventDefault();
          event.dataTransfer.dropEffect = "copy";
        }}
        onDragLeave={(event) => {
          if (!containsDraggedNodeKind(event.dataTransfer)) return;
          paletteDragDepth.current = Math.max(0, paletteDragDepth.current - 1);
          if (paletteDragDepth.current === 0) setPaletteDragOver(false);
        }}
        onDrop={(event) => {
          const kind = readDraggedNodeKind(event.dataTransfer);
          if (!kind) return;
          event.preventDefault();
          paletteDragDepth.current = 0;
          setPaletteDragOver(false);

          const viewport = event.currentTarget;
          const bounds = viewport.getBoundingClientRect();
          const rawX = (event.clientX - bounds.left + viewport.scrollLeft) / zoom - defaultWidth / 2;
          const rawY = (event.clientY - bounds.top + viewport.scrollTop) / zoom - defaultHeight / 2;
          const x = Math.min(canvasWidth - defaultWidth - 12, Math.max(12, Math.round(rawX / 4) * 4));
          const y = Math.min(canvasHeight - defaultHeight - 12, Math.max(12, Math.round(rawY / 4) * 4));
          onAddNode(kind, { x, y }, activeGroupName);
        }}
      >
        <div className="canvas-stage" style={{ width: canvasWidth * zoom, height: canvasHeight * zoom }}>
        <div className="canvas-world" style={{ transform: `scale(${zoom})` }}>
          {showRelations && (
            <svg className="relation-layer" width={canvasWidth} height={canvasHeight} aria-label="Node relations">
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
                const curve = Math.max(42, Math.abs(points.target.x - points.source.x) * 0.38);
                const direction = points.target.x >= points.source.x ? 1 : -1;
                const path = `M ${points.source.x} ${points.source.y} C ${points.source.x + curve * direction} ${points.source.y}, ${points.target.x - curve * direction} ${points.target.y}, ${points.target.x} ${points.target.y}`;
                const definition = getRelationDefinition(relation.kind);
                return (
                  <g
                    key={relation.id}
                    className={`relation relation--${relation.kind} ${selectedRelationId === relation.id ? "relation--selected" : ""}`}
                  >
                    <path className="relation-line" d={path} markerEnd={`url(#arrow-${relation.kind})`} />
                    <path
                      className="relation-hit"
                      d={path}
                      onClick={() => openRelationEditor(relation.id)}
                      aria-label={`Edit ${definition.label} relation`}
                    />
                    <text
                      x={(points.source.x + points.target.x) / 2}
                      y={(points.source.y + points.target.y) / 2 - 8}
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
                  if (event.button !== 0) return;
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
                  const x = Math.min(canvasWidth - defaultWidth - 12, Math.max(12, Math.round(rawX / 4) * 4));
                  const y = Math.min(canvasHeight - defaultHeight - 12, Math.max(12, Math.round(rawY / 4) * 4));
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
              <strong>{activeAreaView === "all" ? "Drag a component onto the canvas" : "This area is empty"}</strong>
              <p>Drop a component here or move an existing node into this area.</p>
            </div>
          )}
        </div>
        </div>
      </div>

      {paletteDragOver && <div className="canvas-drop-hint">Drop to place component</div>}

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

      {showRelations && (
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
