import type { EtabProjectDocument, LayoutGroup } from "./model";

export type AreaView = "all" | "unassigned" | `group:${string}`;

export function areaViewForGroup(groupName: string): AreaView {
  return `group:${groupName}`;
}

export function groupNameFromAreaView(view: AreaView): string | undefined {
  return view.startsWith("group:") ? view.slice("group:".length) : undefined;
}

export function getLayoutGroups(document: EtabProjectDocument): LayoutGroup[] {
  const groups = new Map<string, LayoutGroup>();
  for (const group of document.layout.groups ?? []) {
    groups.set(group.name.toLowerCase(), group);
  }
  for (const layout of document.layout.nodes) {
    if (!layout.group || groups.has(layout.group.toLowerCase())) continue;
    groups.set(layout.group.toLowerCase(), {
      name: layout.group,
      displayName: formatAreaName(layout.group),
    });
  }
  return Array.from(groups.values());
}

export function nodeGroup(document: EtabProjectDocument, nodeId: string): string | undefined {
  return document.layout.nodes.find((layout) => layout.nodeId === nodeId)?.group;
}

export function nodeMatchesArea(document: EtabProjectDocument, nodeId: string, view: AreaView): boolean {
  if (view === "all") return true;
  const group = nodeGroup(document, nodeId);
  if (view === "unassigned") return !group;
  return group?.toLowerCase() === groupNameFromAreaView(view)?.toLowerCase();
}

export function createUniqueAreaName(displayName: string, groups: LayoutGroup[]): string {
  const words = displayName
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .match(/[A-Za-z0-9]+/g) ?? [];
  let base = words.map((word) => word[0]?.toUpperCase() + word.slice(1)).join("") || "Area";
  if (!/^[A-Za-z_]/.test(base)) base = `Area${base}`;
  const used = new Set(groups.map((group) => group.name.toLowerCase()));
  let candidate = base;
  let suffix = 2;
  while (used.has(candidate.toLowerCase())) candidate = `${base}${suffix++}`;
  return candidate;
}

function formatAreaName(name: string): string {
  return name
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/^./, (character) => character.toUpperCase());
}
