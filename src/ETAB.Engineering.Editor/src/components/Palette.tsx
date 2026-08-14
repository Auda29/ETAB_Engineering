import { useState } from "react";
import type { NodeKind } from "../model";
import { nodeKindLabels } from "../modelFactory";
import { nodeKindDragType } from "../nodeDragDrop";

const palette: Array<{ kind: NodeKind; icon: string; description: string }> = [
  { kind: "applicationUnit", icon: "AU", description: "State-model machine unit" },
  { kind: "commandUnit", icon: "CU", description: "Sequence coordinator" },
  { kind: "recipeManager", icon: "RM", description: "Recipe persistence adapter" },
  { kind: "machineLink", icon: "ML", description: "Machine-to-machine link" },
];

export function Palette({ onAdd }: { onAdd: (kind: NodeKind) => void }) {
  const [draggingKind, setDraggingKind] = useState<NodeKind>();

  return (
    <section className="sidebar-section">
      <div className="section-heading">
        <span>Component palette</span>
        <span className="section-heading__meta">v0.1</span>
      </div>
      <div className="palette">
        {palette.map((item) => (
          <button
            type="button"
            data-testid={`add-${item.kind}`}
            className={`palette-card palette-card--${item.kind} ${draggingKind === item.kind ? "palette-card--dragging" : ""}`}
            key={item.kind}
            draggable
            title={`Drag ${nodeKindLabels[item.kind]} onto the machine canvas. Press Enter to add it automatically.`}
            aria-label={`Drag ${nodeKindLabels[item.kind]} onto the machine canvas`}
            onDragStart={(event) => {
              event.dataTransfer.setData(nodeKindDragType, item.kind);
              event.dataTransfer.effectAllowed = "copy";
              setDraggingKind(item.kind);
            }}
            onDragEnd={() => setDraggingKind(undefined)}
            onKeyDown={(event) => {
              if (event.key !== "Enter" && event.key !== " ") return;
              event.preventDefault();
              onAdd(item.kind);
            }}
          >
            <span className="palette-card__icon">{item.icon}</span>
            <span>
              <strong>{nodeKindLabels[item.kind]}</strong>
              <small>{item.description}</small>
            </span>
            <span className="palette-card__drag" aria-hidden="true">⠿</span>
          </button>
        ))}
      </div>
    </section>
  );
}
