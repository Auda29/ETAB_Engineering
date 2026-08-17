import type {
  EtabCommand,
  EtabField,
  EtabNode,
  EtabProjectDocument,
  NodeKind,
} from "./model";

export const nodeKindLabels: Record<NodeKind, string> = {
  applicationUnit: "Application Unit",
  commandUnit: "Command Unit",
  recipeManager: "Recipe Manager",
  machineLink: "Machine Link",
};

export function createCommand(existing: EtabCommand[]): EtabCommand {
  const noActionMissing = !existing.some((command) => command.name === "NoAction");
  const enumValue = noActionMissing
    ? 0
    : Math.max(0, ...existing.map((command) => command.enumValue)) + 10;
  const suffix = existing.length + 1;
  return {
    id: crypto.randomUUID().toLowerCase(),
    name: noActionMissing ? "NoAction" : `Command${suffix}`,
    displayName: noActionMissing ? "No Action" : `Command ${suffix}`,
    enumValue,
    etabCommand: noActionMissing ? "NoAction" : "User",
  };
}

export function createField(existing: EtabField[], stem: string): EtabField {
  const suffix = existing.length + 1;
  return {
    id: crypto.randomUUID().toLowerCase(),
    name: `${stem}${suffix}`,
    dataType: "BOOL",
  };
}

export function createNode(kind: NodeKind, document: EtabProjectDocument): EtabNode {
  const ordinal = document.nodes.length + 1;
  const label = nodeKindLabels[kind];
  const stem = kind[0].toUpperCase() + kind.slice(1).replace("Unit", "");
  const node: EtabNode = {
    id: crypto.randomUUID().toLowerCase(),
    kind,
    name: `${stem}${ordinal}`,
    symbolStem: `${stem}${ordinal}`,
    displayName: `${label} ${ordinal}`,
    role: `${kind}${ordinal}`,
    generate: {
      commandEnum: kind === "applicationUnit" || kind === "commandUnit",
      requestType: kind === "applicationUnit" || kind === "commandUnit",
      statusType: true,
      baseFunctionBlock: kind === "applicationUnit",
      instance: document.project.generation.relationWiring ?? false,
    },
    commands: [],
    requestPayload: [],
    statusPayload: [],
    mtp: { exposed: false, procedures: [] },
  };

  if (node.generate.commandEnum) node.commands.push(createCommand(node.commands));
  applyKindDefaults(node, kind);
  return node;
}

export function applyKindDefaults(node: EtabNode, kind: NodeKind): void {
  node.kind = kind;
  delete node.generate.instanceType;
  delete node.generate.relationStatusMember;
  node.generate.callInProgram = false;
  delete node.applicationUnit;
  delete node.commandUnit;
  delete node.recipeManager;
  delete node.machineLink;

  if (kind === "applicationUnit") {
    node.applicationUnit = {
      startMode: "ET.eMODE.AUTO",
      homingMode: "ET.eMODE.INIT",
      stopMode: "ET.eMODE.IDLE",
      keepRemoteControl: false,
      setMachineErrorOnCommandError: true,
      command: { startState: 10, resetErrorOnStart: true },
    };
  } else if (kind === "commandUnit") {
    node.commandUnit = { startState: 10, resetErrorOnStart: true };
    node.generate.baseFunctionBlock = false;
  } else if (kind === "recipeManager") {
    node.recipeManager = {
      dataType: "BYTE",
      filePath: "C:\\TwinCAT\\Recipes",
      fileName: "Recipe.xml",
      xPath: "/Recipe",
      enableAutoSave: false,
      enableBackupFile: true,
      requireExternalValidation: true,
    };
    disableCommandGeneration(node);
  } else {
    node.machineLink = {
      bridgeType: "GenericBridge",
      isPrimary: true,
      watchdogTime: "T#2S",
      primaryWinsTie: true,
      allowTokenWithoutPartnerAlive: false,
      clearTxWhenDisabled: true,
    };
    disableCommandGeneration(node);
  }
}

function disableCommandGeneration(node: EtabNode): void {
  node.generate.commandEnum = false;
  node.generate.requestType = true;
  node.generate.baseFunctionBlock = false;
}

export function getLayout(document: EtabProjectDocument, nodeId: string) {
  return document.layout.nodes.find((layout) => layout.nodeId === nodeId);
}
