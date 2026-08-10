import { useMemo, useRef, useState } from "react";
import type { EtabProjectDocument, NodeLayout } from "../model";
import { nodeKindLabels } from "../modelFactory";

const defaultWidth = 222;
const defaultHeight = 112;

interface DragState {
  nodeId: string;
  startPointerX: number;
  startPointerY: number;
  startX: number;
  startY: number;
}

export function MachineCanvas({
  document,
  selectedNodeId,
  onSelect,
  onMoveNode,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  onSelect: (id: string) => void;
  onMoveNode: (id: string, x: number, y: number) => void;
}) {
  const [showRelations, setShowRelations] = useState(true);
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

  return (
    <main className="canvas-shell">
      <div className="canvas-toolbar">
        <div>
          <strong>Machine canvas</strong>
          <span>Drag nodes to update visual layout</span>
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
                <marker id="arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto" markerUnits="strokeWidth">
                  <path d="M0,0 L8,4 L0,8 Z" className="relation-arrow" />
                </marker>
              </defs>
              {document.relations.map((relation) => {
                const source = layouts.get(relation.sourceNodeId);
                const target = layouts.get(relation.targetNodeId);
                if (!source || !target) return null;
                const sx = source.x + (source.width ?? defaultWidth) / 2;
                const sy = source.y + (source.height ?? defaultHeight) / 2;
                const tx = target.x + (target.width ?? defaultWidth) / 2;
                const ty = target.y + (target.height ?? defaultHeight) / 2;
                const curve = Math.max(50, Math.abs(tx - sx) * 0.38);
                const path = `M ${sx} ${sy} C ${sx + curve} ${sy}, ${tx - curve} ${ty}, ${tx} ${ty}`;
                return (
                  <g key={relation.id} className={`relation relation--${relation.kind}`}>
                    <path d={path} markerEnd="url(#arrow)" />
                    <text x={(sx + tx) / 2} y={(sy + ty) / 2 - 7}>{relation.label || relation.kind}</text>
                  </g>
                );
              })}
            </svg>
          )}

          {document.nodes.map((node) => {
            const layout = layouts.get(node.id)!;
            const selected = selectedNodeId === node.id;
            return (
              <article
                data-testid={`canvas-node-${node.name}`}
                key={node.id}
                className={`canvas-node canvas-node--${node.kind} ${selected ? "canvas-node--selected" : ""}`}
                style={{
                  left: layout.x,
                  top: layout.y,
                  width: layout.width ?? defaultWidth,
                  minHeight: layout.height ?? defaultHeight,
                }}
                onPointerDown={(event) => {
                  if (event.button !== 0) return;
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
    </main>
  );
}
