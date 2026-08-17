export type NodeKind = "applicationUnit" | "commandUnit" | "recipeManager" | "machineLink";
export type RelationKind = "contains" | "commands" | "observes" | "usesRecipe" | "usesLink";
export type EtabCommandMapping = "NoAction" | "Reset" | "Start" | "Homing" | "Stop" | "Abort" | "Clear" | "User";

export interface EtabProjectDocument {
  schemaVersion: "0.1";
  project: EtabProject;
  nodes: EtabNode[];
  relations: EtabRelation[];
  layout: EtabLayout;
}

export interface EtabProject {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  prefix: string;
  namespace: string;
  etabLibrary: { placeholder: string; version: string };
  twinCAT: { version: string; plcProject?: string };
  generation: {
    generatedRoot: string;
    applicationRoot: string;
    createUserStubs: boolean;
    programCallStructure?: boolean;
    relationWiring?: boolean;
    runtimeExecution?: boolean;
  };
}

export interface EtabNode {
  id: string;
  kind: NodeKind;
  name: string;
  symbolStem: string;
  displayName: string;
  description?: string;
  role: string;
  generate: NodeGenerationSettings;
  commands: EtabCommand[];
  requestPayload: EtabField[];
  statusPayload: EtabField[];
  applicationUnit?: ApplicationUnitSettings;
  commandUnit?: CommandUnitSettings;
  recipeManager?: RecipeManagerSettings;
  machineLink?: MachineLinkSettings;
  mtp?: MtpSettings;
}

export interface NodeGenerationSettings {
  commandEnum: boolean;
  requestType: boolean;
  statusType: boolean;
  baseFunctionBlock: boolean;
  instance: boolean;
  instanceType?: string;
  relationStatusMember?: string;
  callInProgram?: boolean;
}

export interface EtabCommand {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  enumValue: number;
  etabCommand: EtabCommandMapping;
}

export interface EtabField {
  id: string;
  name: string;
  dataType: string;
  arrayDimensions?: ArrayDimension[];
  description?: string;
  defaultValue?: string;
}

export interface ArrayDimension { lower: number; upper: number }

export interface CommandUnitSettings { startState: number; resetErrorOnStart: boolean }

export interface ApplicationUnitSettings {
  startMode: string;
  homingMode: string;
  stopMode: string;
  keepRemoteControl: boolean;
  setMachineErrorOnCommandError: boolean;
  command: CommandUnitSettings;
}

export interface RecipeManagerSettings {
  dataType: string;
  filePath: string;
  fileName: string;
  xPath: string;
  enableAutoSave?: boolean;
  enableBackupFile?: boolean;
  requireExternalValidation?: boolean;
}

export interface MachineLinkSettings {
  bridgeType: "GenericBridge" | "EL6695" | "EL6692" | "ExternalBridge";
  isPrimary: boolean;
  watchdogTime: string;
  primaryWinsTie: boolean;
  allowTokenWithoutPartnerAlive: boolean;
  clearTxWhenDisabled: boolean;
}

export interface MtpSettings {
  exposed: boolean;
  serviceName?: string;
  procedures: Array<{ id: string; name: string; procedureId: number; commandId: string }>;
}

export interface EtabRelation {
  id: string;
  kind: RelationKind;
  sourceNodeId: string;
  targetNodeId: string;
  label?: string;
}

export interface EtabLayout {
  groups?: LayoutGroup[];
  nodes: NodeLayout[];
}

export interface LayoutGroup {
  name: string;
  displayName: string;
}

export interface NodeLayout {
  nodeId: string;
  x: number;
  y: number;
  width?: number;
  height?: number;
  group?: string;
}

export interface ValidationIssue { code: string; path: string; message: string }
export interface ValidationResponse { isValid: boolean; issues: ValidationIssue[] }

export interface SessionResponse {
  workspaceRoot: string;
  exampleProjectPath: string;
  supportsNativeFileDialogs: boolean;
}

export interface ProjectFileDialogResponse {
  canceled: boolean;
  path?: string;
}

export interface ConnectedPlcProjectResponse {
  path: string;
  projectRoot: string;
  plcProjectPath: string;
  created: boolean;
  document: EtabProjectDocument;
  validation: ValidationResponse;
}

export interface ConnectPlcProjectDialogResponse {
  canceled: boolean;
  project?: ConnectedPlcProjectResponse;
}

export interface NewProjectResponse {
  document: EtabProjectDocument;
  validation: ValidationResponse;
}

export interface OpenProjectResponse {
  path: string;
  projectRoot: string;
  document: EtabProjectDocument;
  validation: ValidationResponse;
}

export interface SaveProjectResponse {
  path: string;
  projectRoot: string;
  sha256: string;
  validation: ValidationResponse;
}

export interface ArtifactPreview {
  sourceModelId: string;
  kind: string;
  name: string;
  twinCatGuid: string;
  relativePath: string;
  sha256: string;
  content: string;
}

export interface PlannedChange {
  changeKind: string;
  artifactKind: string;
  sourceModelId: string;
  relativePath: string;
  previousRelativePath?: string;
  message?: string;
}

export interface PreviewResponse {
  validation: ValidationResponse;
  projectId?: string;
  projectName?: string;
  projectRoot?: string;
  generatedRoot?: string;
  hasConflicts: boolean;
  artifacts: ArtifactPreview[];
  changes: PlannedChange[];
  manifest?: { changeKind: string; relativePath: string; message?: string; content: string };
  projectFile?: { changeKind: string; relativePath: string; message?: string; content: string };
  projectIntegrationManifest?: { changeKind: string; relativePath: string; message?: string; content: string };
  taskFile?: { changeKind: string; relativePath: string; message?: string; content: string };
  confirmationToken?: string;
  integrateProject: boolean;
  issues: ValidationIssue[];
}

export interface GenerateProjectResponse {
  success: boolean;
  projectRoot: string;
  created: number;
  updated: number;
  renamed: number;
  deleted: number;
  projectFileChanged: boolean;
  taskFileChanged: boolean;
  manifestChanged: boolean;
  issues: Array<{ code: string; message: string }>;
}
