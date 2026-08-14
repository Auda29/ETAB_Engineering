import { useEffect, useMemo, useRef, useState } from "react";
import type { EtabProjectDocument, NodeLayout, RelationKind } from "../model";
import { nodeKindLabels } from "../modelFactory";
import {
  getAvailableRelationKinds,
  getRelationDefinition,
  hasConnectableTarget,
} from "../relationRules";

const defaultWidth = 222;
const defaultHeight = 112;

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

export function MachineCanvas({
  document,
  selectedNodeId,
  onSelect,
  onMoveNode,
  onAddRelation,
  onUpdateRelation,
  onDeleteRelation,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  onSelect: (id: string) => void;
  onMoveNode: (id: string, x: number, y: number) => void;
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
  const drag = useRef<DragState | undefined>(undefined);

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

  const sourceNode = document.nodes.find((node) => node.id === connectSourceId);
  const targetNode = document.nodes.find((node) => node.id === connectTargetId);
  const connectKinds = connectSourceId && connectTargetId
    ? getAvailableRelationKinds(document, connectSourceId, connectTargetId)
    : [];
  const selectedRelation = document.relations.find((relation) => relation.id === selectedRelationId);
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
    };
    window.addEventListener("keydown", cancel);
    return () => window.removeEventListener("keydown", cancel);
  }, []);

  useEffect(() => {
    if (!selectedRelationId) return;
    const relation = document.relations.find((item) => item.id === selectedRelationId);
    if (!relation) setSelectedRelationId(undefined);
  }, [document.relations, selectedRelationId]);

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

  return (
    <main className="canvas-shell">
      <div className="canvas-toolbar">
        <div>
          <strong>Machine canvas</strong>
          <span>{connectSourceId ? "Choose a highlighted target node" : "Drag nodes or connect them directly"}</span>
        </div>
        <label className="canvas-toggle">
          <input type="checkbox" checked={showRelations} onChange={(event) => setShowRelations(event.target.checked)} />
          Relations
        </label>
      </div>

      <div className="canvas-viewport" data-testid="machine-canvas">
        <div className="canvas-world">
          {showRelations && (
            <svg className="relation-layer" width="1800" height="1100" aria-label="Node relations">
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
              {document.relations.map((relation) => {
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

          {document.nodes.map((node) => {
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
                  const x = Math.max(12, Math.round((drag.current.startX + event.clientX - drag.current.startPointerX) / 4) * 4);
                  const y = Math.max(12, Math.round((drag.current.startY + event.clientY - drag.current.startPointerY) / 4) * 4);
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

          {document.nodes.length === 0 && (
            <div className="canvas-empty">
              <span>+</span>
              <strong>Add a component from the palette</strong>
              <p>The semantic validator remains active while the model is incomplete.</p>
            </div>
          )}
        </div>
      </div>

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
