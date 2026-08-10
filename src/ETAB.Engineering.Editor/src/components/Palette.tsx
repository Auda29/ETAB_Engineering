import type { NodeKind } from "../model";
import { nodeKindLabels } from "../modelFactory";

const palette: Array<{ kind: NodeKind; icon: string; description: string }> = [
  { kind: "applicationUnit", icon: "AU", description: "State-model machine unit" },
  { kind: "commandUnit", icon: "CU", description: "Sequence coordinator" },
  { kind: "recipeManager", icon: "RM", description: "Recipe persistence adapter" },
  { kind: "machineLink", icon: "ML", description: "Machine-to-machine link" },
];

export function Palette({ onAdd }: { onAdd: (kind: NodeKind) => void }) {
  return (
    <section className="sidebar-section">
      <div className="section-heading">
        <span>Component palette</span>
        <span className="section-heading__meta">v0.1</span>
      </div>
      <div className="palette">
        {palette.map((item) => (
          <button
            data-testid={`add-${item.kind}`}
            className={`palette-card palette-card--${item.kind}`}
            key={item.kind}
            onClick={() => onAdd(item.kind)}
          >
            <span className="palette-card__icon">{item.icon}</span>
            <span>
              <strong>{nodeKindLabels[item.kind]}</strong>
              <small>{item.description}</small>
            </span>
            <span className="palette-card__add">+</span>
          </button>
        ))}
      </div>
    </section>
  );
}
