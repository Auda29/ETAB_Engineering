import type {
  EtabNode,
  EtabProjectDocument,
  NodeKind,
  RelationKind,
} from "./model";

export interface RelationDefinition {
  kind: RelationKind;
  label: string;
  description: string;
  sourceKinds: NodeKind[];
  targetKinds: NodeKind[];
}

const unitKinds: NodeKind[] = ["applicationUnit", "commandUnit"];

export const relationDefinitions: RelationDefinition[] = [
  {
    kind: "contains",
    label: "Contains",
    description: "Places a child unit below an Application Unit in the machine hierarchy.",
    sourceKinds: ["applicationUnit"],
    targetKinds: unitKinds,
  },
  {
    kind: "commands",
    label: "Commands",
    description: "The source unit sends commands or requests to the target unit.",
    sourceKinds: unitKinds,
    targetKinds: unitKinds,
  },
  {
    kind: "observes",
    label: "Observes",
    description: "The source unit reads or evaluates the target unit status.",
    sourceKinds: unitKinds,
    targetKinds: unitKinds,
  },
  {
    kind: "usesRecipe",
    label: "Uses recipe",
    description: "The source unit consumes data provided by a Recipe Manager.",
    sourceKinds: unitKinds,
    targetKinds: ["recipeManager"],
  },
  {
    kind: "usesLink",
    label: "Uses machine link",
    description: "The source unit communicates through a Machine Link.",
    sourceKinds: unitKinds,
    targetKinds: ["machineLink"],
  },
];

export function getRelationDefinition(kind: RelationKind): RelationDefinition {
  return relationDefinitions.find((definition) => definition.kind === kind)!;
}

export function getSourceRelationDefinitions(node: EtabNode): RelationDefinition[] {
  return relationDefinitions.filter((definition) => definition.sourceKinds.includes(node.kind));
}

export function getEligibleTargets(
  document: EtabProjectDocument,
  sourceId: string,
  kind: RelationKind,
  ignoredRelationId?: string,
): EtabNode[] {
  return document.nodes.filter((target) =>
    getAvailableRelationKinds(document, sourceId, target.id, ignoredRelationId).includes(kind));
}

export function getAvailableRelationKinds(
  document: EtabProjectDocument,
  sourceId: string,
  targetId: string,
  ignoredRelationId?: string,
): RelationKind[] {
  const source = document.nodes.find((node) => node.id === sourceId);
  const target = document.nodes.find((node) => node.id === targetId);
  if (!source || !target || source.id === target.id) return [];

  return relationDefinitions
    .filter((definition) =>
      definition.sourceKinds.includes(source.kind) &&
      definition.targetKinds.includes(target.kind))
    .filter((definition) => !document.relations.some((relation) =>
      relation.id !== ignoredRelationId &&
      relation.kind === definition.kind &&
      relation.sourceNodeId === sourceId &&
      relation.targetNodeId === targetId))
    .filter((definition) => definition.kind !== "contains" || canContain(
      document,
      sourceId,
      targetId,
      ignoredRelationId,
    ))
    .map((definition) => definition.kind);
}

export function hasConnectableTarget(document: EtabProjectDocument, sourceId: string): boolean {
  return document.nodes.some((target) =>
    target.id !== sourceId && getAvailableRelationKinds(document, sourceId, target.id).length > 0);
}

function canContain(
  document: EtabProjectDocument,
  sourceId: string,
  targetId: string,
  ignoredRelationId?: string,
): boolean {
  const alreadyHasParent = document.relations.some((relation) =>
    relation.id !== ignoredRelationId &&
    relation.kind === "contains" &&
    relation.targetNodeId === targetId);
  if (alreadyHasParent) return false;

  const childrenByParent = new Map<string, string[]>();
  for (const relation of document.relations) {
    if (relation.id === ignoredRelationId || relation.kind !== "contains") continue;
    const children = childrenByParent.get(relation.sourceNodeId) ?? [];
    children.push(relation.targetNodeId);
    childrenByParent.set(relation.sourceNodeId, children);
  }

  const pending = [targetId];
  const visited = new Set<string>();
  while (pending.length > 0) {
    const nodeId = pending.pop()!;
    if (nodeId === sourceId) return false;
    if (visited.has(nodeId)) continue;
    visited.add(nodeId);
    pending.push(...(childrenByParent.get(nodeId) ?? []));
  }

  return true;
}
