import type { NodeKind } from "./model";

export const nodeKindDragType = "application/x-etab-node-kind";

const nodeKinds: NodeKind[] = [
  "applicationUnit",
  "commandUnit",
  "recipeManager",
  "machineLink",
];

export function readDraggedNodeKind(dataTransfer: DataTransfer): NodeKind | undefined {
  const value = dataTransfer.getData(nodeKindDragType);
  return nodeKinds.find((kind) => kind === value);
}

export function containsDraggedNodeKind(dataTransfer: DataTransfer): boolean {
  return Array.from(dataTransfer.types).includes(nodeKindDragType);
}
