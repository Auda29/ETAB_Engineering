import { useMemo, useState } from "react";
import type { EtabNode, EtabProjectDocument } from "../model";
import { nodeKindLabels } from "../modelFactory";

interface TreeRow { node: EtabNode; depth: number }

function createTreeRows(document: EtabProjectDocument): TreeRow[] {
  const children = new Map<string, string[]>();
  const childIds = new Set<string>();
  for (const relation of document.relations.filter((item) => item.kind === "contains")) {
    const list = children.get(relation.sourceNodeId) ?? [];
    list.push(relation.targetNodeId);
    children.set(relation.sourceNodeId, list);
    childIds.add(relation.targetNodeId);
  }
  const byId = new Map(document.nodes.map((node) => [node.id, node]));
  const visited = new Set<string>();
  const rows: TreeRow[] = [];
  const visit = (node: EtabNode, depth: number) => {
    if (visited.has(node.id)) return;
    visited.add(node.id);
    rows.push({ node, depth });
    for (const childId of children.get(node.id) ?? []) {
      const child = byId.get(childId);
      if (child) visit(child, depth + 1);
    }
  };
  document.nodes.filter((node) => !childIds.has(node.id)).forEach((node) => visit(node, 0));
  document.nodes.forEach((node) => visit(node, 0));
  return rows;
}

export function ProjectTree({
  document,
  selectedNodeId,
  onSelect,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  onSelect: (id?: string) => void;
}) {
  const [query, setQuery] = useState("");
  const rows = useMemo(() => createTreeRows(document), [document]);
  const filtered = query.trim()
    ? rows.filter(({ node }) => `${node.displayName} ${node.name} ${node.role}`.toLowerCase().includes(query.toLowerCase()))
    : rows;

  return (
    <section className="sidebar-section sidebar-section--tree">
      <div className="section-heading">
        <span>Machine structure</span>
        <button className="link-button" onClick={() => onSelect(undefined)}>Project</button>
      </div>
      <input
        className="tree-search"
        placeholder="Filter nodes…"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
      />
      <div className="tree" data-testid="project-tree">
        {filtered.map(({ node, depth }) => (
          <button
            data-testid={`tree-node-${node.name}`}
            key={node.id}
            className={`tree-row ${selectedNodeId === node.id ? "tree-row--selected" : ""}`}
            style={{ paddingLeft: 12 + depth * 18 }}
            onClick={() => onSelect(node.id)}
          >
            <span className={`kind-dot kind-dot--${node.kind}`} />
            <span className="tree-row__copy">
              <strong>{node.displayName}</strong>
              <small>{nodeKindLabels[node.kind]} · {node.role}</small>
            </span>
          </button>
        ))}
      </div>
      <div className="tree-summary">
        <span>{document.nodes.length} nodes</span>
        <span>{document.relations.length} relations</span>
      </div>
    </section>
  );
}
