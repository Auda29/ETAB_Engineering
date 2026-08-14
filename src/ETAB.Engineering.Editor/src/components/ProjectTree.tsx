import { useMemo, useState } from "react";
import {
  areaViewForGroup,
  getLayoutGroups,
  nodeGroup,
  type AreaView,
} from "../areaModel";
import type { EtabNode, EtabProjectDocument } from "../model";
import { nodeKindLabels } from "../modelFactory";

interface TreeRow { node: EtabNode; depth: number }
interface TreeSection { view: AreaView; label: string; rows: TreeRow[]; groupName?: string }

interface AreaEditorState {
  name: string;
  displayName: string;
}

function createTreeRows(document: EtabProjectDocument, nodes: EtabNode[]): TreeRow[] {
  const nodeIds = new Set(nodes.map((node) => node.id));
  const children = new Map<string, string[]>();
  const childIds = new Set<string>();
  for (const relation of document.relations.filter((item) =>
    item.kind === "contains" && nodeIds.has(item.sourceNodeId) && nodeIds.has(item.targetNodeId))) {
    const list = children.get(relation.sourceNodeId) ?? [];
    list.push(relation.targetNodeId);
    children.set(relation.sourceNodeId, list);
    childIds.add(relation.targetNodeId);
  }
  const byId = new Map(nodes.map((node) => [node.id, node]));
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
  nodes.filter((node) => !childIds.has(node.id)).forEach((node) => visit(node, 0));
  nodes.forEach((node) => visit(node, 0));
  return rows;
}

export function ProjectTree({
  document,
  selectedNodeId,
  activeAreaView,
  onSelect,
  onSelectArea,
  onRenameArea,
  onDeleteArea,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  activeAreaView: AreaView;
  onSelect: (id?: string) => void;
  onSelectArea: (view: AreaView) => void;
  onRenameArea: (name: string, displayName: string) => void;
  onDeleteArea: (name: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [collapsed, setCollapsed] = useState<Set<AreaView>>(() => new Set());
  const [areaEditor, setAreaEditor] = useState<AreaEditorState>();
  const groups = useMemo(() => getLayoutGroups(document), [document]);
  const sections = useMemo<TreeSection[]>(() => {
    const groupSections: TreeSection[] = groups.map((group) => ({
      view: areaViewForGroup(group.name),
      label: group.displayName,
      groupName: group.name,
      rows: createTreeRows(document, document.nodes.filter((node) =>
        nodeGroup(document, node.id)?.toLowerCase() === group.name.toLowerCase())),
    }));
    const unassigned = document.nodes.filter((node) => !nodeGroup(document, node.id));
    if (unassigned.length > 0 || groups.length === 0) {
      groupSections.push({
        view: "unassigned",
        label: "Unassigned",
        rows: createTreeRows(document, unassigned),
      });
    }
    return groupSections;
  }, [document, groups]);
  const normalizedQuery = query.trim().toLowerCase();

  const toggleSection = (view: AreaView) => {
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(view)) next.delete(view);
      else next.add(view);
      return next;
    });
  };

  return (
    <section className="sidebar-section sidebar-section--tree">
      <div className="section-heading">
        <span>Machine structure</span>
        <button className="link-button" onClick={() => {
          setAreaEditor(undefined);
          onSelect(undefined);
          onSelectArea("all");
        }}>Project</button>
      </div>
      <input
        className="tree-search"
        placeholder="Filter nodes…"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
      />
      <div className="tree" data-testid="project-tree">
        {sections.map((section) => {
          const rows = normalizedQuery
            ? section.rows.filter(({ node }) =>
              `${node.displayName} ${node.name} ${node.role}`.toLowerCase().includes(normalizedQuery))
            : section.rows;
          if (normalizedQuery && rows.length === 0) return null;
          const isCollapsed = collapsed.has(section.view) && !normalizedQuery;
          const groupName = section.groupName;
          const editedArea = areaEditor?.name.toLowerCase() === groupName?.toLowerCase()
            ? areaEditor
            : undefined;
          return (
            <div className="tree-section" key={section.view}>
              <div className={`tree-folder ${groupName ? "tree-folder--editable" : ""} ${activeAreaView === section.view ? "tree-folder--active" : ""}`}>
                <button className="tree-folder__main" type="button" onClick={() => onSelectArea(section.view)}>
                  <span className="tree-folder__icon">▰</span>
                  <strong>{section.label}</strong>
                  <span>{section.rows.length}</span>
                </button>
                {groupName && (
                  <button
                    className={`tree-folder__settings ${editedArea ? "tree-folder__settings--active" : ""}`}
                    type="button"
                    title={`Rename or remove ${section.label}`}
                    aria-label={`Rename or remove ${section.label} area`}
                    aria-expanded={Boolean(editedArea)}
                    onClick={() => {
                      onSelectArea(section.view);
                      setAreaEditor(editedArea ? undefined : {
                        name: groupName,
                        displayName: section.label,
                      });
                    }}
                  >•••</button>
                )}
                <button
                  className="tree-folder__toggle"
                  type="button"
                  title={isCollapsed ? "Expand area" : "Collapse area"}
                  aria-label={`${isCollapsed ? "Expand" : "Collapse"} ${section.label}`}
                  onClick={() => toggleSection(section.view)}
                >{isCollapsed ? "›" : "⌄"}</button>
              </div>
              {editedArea && groupName && (
                <form className="tree-area-editor" onSubmit={(event) => {
                  event.preventDefault();
                  if (!editedArea.displayName.trim()) return;
                  onRenameArea(groupName, editedArea.displayName);
                  setAreaEditor(undefined);
                }}>
                  <label>
                    <span>Area name</span>
                    <input
                      autoFocus
                      value={editedArea.displayName}
                      aria-label={`${section.label} area name`}
                      onChange={(event) => setAreaEditor({ ...editedArea, displayName: event.target.value })}
                      onKeyDown={(event) => {
                        if (event.key === "Escape") setAreaEditor(undefined);
                      }}
                    />
                  </label>
                  <div className="tree-area-editor__actions">
                    <button className="tree-area-editor__delete" type="button" onClick={() => {
                      setAreaEditor(undefined);
                      onDeleteArea(groupName);
                    }}>Remove</button>
                    <button type="button" onClick={() => setAreaEditor(undefined)}>Cancel</button>
                    <button type="submit" disabled={!editedArea.displayName.trim()}>Save</button>
                  </div>
                </form>
              )}
              {!isCollapsed && rows.map(({ node, depth }) => (
                <button
                  data-testid={`tree-node-${node.name}`}
                  key={node.id}
                  className={`tree-row tree-row--grouped ${selectedNodeId === node.id ? "tree-row--selected" : ""}`}
                  style={{ paddingLeft: 27 + depth * 18 }}
                  onClick={() => onSelect(node.id)}
                >
                  <span className={`kind-dot kind-dot--${node.kind}`} />
                  <span className="tree-row__copy">
                    <strong>{node.displayName}</strong>
                    <small>{nodeKindLabels[node.kind]} · {node.role}</small>
                  </span>
                </button>
              ))}
              {!isCollapsed && rows.length === 0 && <div className="tree-folder__empty">Drop or move nodes into this area</div>}
            </div>
          );
        })}
      </div>
      <div className="tree-summary">
        <span>{document.nodes.length} nodes</span>
        <span>{groups.length} areas · {document.relations.length} relations</span>
      </div>
    </section>
  );
}
