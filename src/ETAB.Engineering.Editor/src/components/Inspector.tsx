import { useEffect, useState } from "react";
import type {
  EtabCommand,
  EtabField,
  EtabNode,
  EtabProjectDocument,
  NodeKind,
  RelationKind,
} from "../model";
import { applyKindDefaults, createCommand, createField, nodeKindLabels } from "../modelFactory";
import {
  getEligibleTargets,
  getRelationDefinition,
  getSourceRelationDefinitions,
} from "../relationRules";
import { Field, IconButton, NumberInput, SelectInput, TextArea, TextInput, Toggle } from "./FormFields";

type DocumentMutation = (document: EtabProjectDocument) => void;
type NodeMutation = (node: EtabNode) => void;
type InspectorTab = "general" | "commands" | "request" | "status" | "relations" | "settings";
type InspectorTabRequest = { nodeId: string; tab: InspectorTab; requestId: string };

export function Inspector({
  document,
  selectedNodeId,
  requestedTab,
  updateDocument,
  updateNode,
  deleteNode,
}: {
  document: EtabProjectDocument;
  selectedNodeId?: string;
  requestedTab?: InspectorTabRequest;
  updateDocument: (mutation: DocumentMutation) => void;
  updateNode: (nodeId: string, mutation: NodeMutation) => void;
  deleteNode: (nodeId: string) => void;
}) {
  const [tab, setTab] = useState<InspectorTab>("general");
  const node = document.nodes.find((item) => item.id === selectedNodeId);
  useEffect(() => setTab("general"), [selectedNodeId]);
  useEffect(() => {
    if (requestedTab && requestedTab.nodeId === selectedNodeId) setTab(requestedTab.tab);
  }, [requestedTab]);

  if (!node) {
    return <ProjectInspector document={document} updateDocument={updateDocument} />;
  }

  const tabs: Array<{ id: InspectorTab; label: string; count?: number }> = [
    { id: "general", label: "General" },
    { id: "commands", label: "Commands", count: node.commands.length },
    { id: "request", label: "Request", count: node.requestPayload.length },
    { id: "status", label: "Status", count: node.statusPayload.length },
    { id: "relations", label: "Relations", count: document.relations.filter((relation) => relation.sourceNodeId === node.id || relation.targetNodeId === node.id).length },
    { id: "settings", label: "Settings" },
  ];

  return (
    <aside className="inspector" data-testid="property-inspector">
      <div className="inspector__title">
        <div>
          <span className={`kind-badge kind-badge--${node.kind}`}>{nodeKindLabels[node.kind]}</span>
          <h2>{node.displayName}</h2>
          <code>{node.id}</code>
        </div>
        <IconButton label="Delete node" danger onClick={() => deleteNode(node.id)}>×</IconButton>
      </div>
      <nav className="inspector-tabs">
        {tabs.map((item) => (
          <button
            data-testid={`inspector-tab-${item.id}`}
            key={item.id}
            className={tab === item.id ? "active" : ""}
            onClick={() => setTab(item.id)}
          >
            {item.label}{item.count !== undefined && <span>{item.count}</span>}
          </button>
        ))}
      </nav>
      <div className="inspector__body">
        {tab === "general" && <NodeGeneral node={node} update={(mutation) => updateNode(node.id, mutation)} />}
        {tab === "commands" && <CommandEditor node={node} update={(mutation) => updateNode(node.id, mutation)} />}
        {tab === "request" && <PayloadEditor title="Request payload" fields={node.requestPayload} stem="requestField" update={(fields) => updateNode(node.id, (draft) => { draft.requestPayload = fields; })} />}
        {tab === "status" && <PayloadEditor title="Status payload" fields={node.statusPayload} stem="statusField" update={(fields) => updateNode(node.id, (draft) => { draft.statusPayload = fields; })} />}
        {tab === "relations" && <RelationEditor document={document} node={node} updateDocument={updateDocument} />}
        {tab === "settings" && <NodeSettings node={node} update={(mutation) => updateNode(node.id, mutation)} />}
      </div>
    </aside>
  );
}

function ProjectInspector({
  document,
  updateDocument,
}: {
  document: EtabProjectDocument;
  updateDocument: (mutation: DocumentMutation) => void;
}) {
  const project = document.project;
  const update = (mutation: (draft: typeof project) => void) => updateDocument((draft) => mutation(draft.project));
  return (
    <aside className="inspector" data-testid="project-inspector">
      <div className="inspector__title inspector__title--project">
        <div>
          <span className="eyebrow">Project properties</span>
          <h2>{project.displayName}</h2>
          <code>schema {document.schemaVersion}</code>
        </div>
      </div>
      <div className="inspector__body form-grid">
        <Field label="IEC project name"><TextInput value={project.name} onChange={(event) => update((draft) => { draft.name = event.target.value; })} /></Field>
        <Field label="Display name"><TextInput value={project.displayName} onChange={(event) => update((draft) => { draft.displayName = event.target.value; })} /></Field>
        <Field label="Prefix"><TextInput value={project.prefix} onChange={(event) => update((draft) => { draft.prefix = event.target.value; })} /></Field>
        <Field label="Namespace"><TextInput value={project.namespace} onChange={(event) => update((draft) => { draft.namespace = event.target.value; })} /></Field>
        <Field label="Description" wide><TextArea rows={3} value={project.description ?? ""} onChange={(event) => update((draft) => setOptional(draft, "description", event.target.value))} /></Field>
        <SectionTitle>ETAB library</SectionTitle>
        <Field label="Placeholder"><TextInput value={project.etabLibrary.placeholder} onChange={(event) => update((draft) => { draft.etabLibrary.placeholder = event.target.value; })} /></Field>
        <Field label="Version"><TextInput value={project.etabLibrary.version} onChange={(event) => update((draft) => { draft.etabLibrary.version = event.target.value; })} /></Field>
        <SectionTitle>TwinCAT target</SectionTitle>
        <Field label="Version"><TextInput value={project.twinCAT.version} onChange={(event) => update((draft) => { draft.twinCAT.version = event.target.value; })} /></Field>
        <Field label="PLC project" hint="Assigned automatically from the selected TwinCAT project." wide><TextInput value={project.twinCAT.plcProject ?? "No PLC project linked"} readOnly /></Field>
        <SectionTitle>Generation boundary</SectionTitle>
        <Field label="PLC output" hint="ETAB writes directly to the TwinCAT DUTs, POUs and GVLs folders."><TextInput value={project.generation.generatedRoot === "." ? "TwinCAT PLC project folders" : project.generation.generatedRoot} readOnly /></Field>
        <Field label="Application root" hint="Managed automatically by the project template."><TextInput value={project.generation.applicationRoot} readOnly /></Field>
        <div className="field field--wide"><Toggle label="Create user stubs" checked={project.generation.createUserStubs} onChange={(checked) => update((draft) => { draft.generation.createUserStubs = checked; })} /><Toggle label="Enable generated runtime execution" checked={project.generation.runtimeExecution ?? false} onChange={(checked) => updateDocument((draft) => { draft.project.generation.runtimeExecution = checked; draft.project.generation.programCallStructure = checked; if (checked) { draft.nodes.forEach((node) => { if (node.kind === "applicationUnit" || node.kind === "commandUnit") { node.generate.instance = true; node.generate.callInProgram = true; } }); } })} /></div>
        <Field label="PLC relation adapter" hint="Creates explicit command and status adapters. Enabling it also enables PLC instances for all currently related nodes." wide><Toggle label="Generate relation wiring" checked={project.generation.relationWiring ?? false} onChange={(checked) => updateDocument((draft) => { draft.project.generation.relationWiring = checked; if (checked) { const relatedIds = new Set(draft.relations.flatMap((relation) => [relation.sourceNodeId, relation.targetNodeId])); draft.nodes.forEach((node) => { if (relatedIds.has(node.id)) node.generate.instance = true; }); } })} /></Field>
      </div>
    </aside>
  );
}

function NodeGeneral({ node, update }: { node: EtabNode; update: (mutation: NodeMutation) => void }) {
  return (
    <div className="form-grid">
      <Field label="Node kind" wide>
        <SelectInput value={node.kind} onChange={(event) => update((draft) => applyKindDefaults(draft, event.target.value as NodeKind))}>
          {Object.entries(nodeKindLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </SelectInput>
      </Field>
      <Field label="PLC name"><TextInput data-testid="node-name" value={node.name} onChange={(event) => update((draft) => { draft.name = event.target.value; })} /></Field>
      <Field label="Symbol stem"><TextInput value={node.symbolStem} onChange={(event) => update((draft) => { draft.symbolStem = event.target.value; })} /></Field>
      <Field label="Display name"><TextInput data-testid="node-display-name" value={node.displayName} onChange={(event) => update((draft) => { draft.displayName = event.target.value; })} /></Field>
      <Field label="Role"><TextInput value={node.role} onChange={(event) => update((draft) => { draft.role = event.target.value; })} /></Field>
      <Field label="Description" wide><TextArea rows={4} value={node.description ?? ""} onChange={(event) => update((draft) => setOptional(draft, "description", event.target.value))} /></Field>
      <Field label="Stable model ID" hint="Renames never change this ID." wide><TextInput value={node.id} readOnly /></Field>
    </div>
  );
}

function CommandEditor({ node, update }: { node: EtabNode; update: (mutation: NodeMutation) => void }) {
  const setCommands = (commands: EtabCommand[]) => update((draft) => { draft.commands = commands; });
  return (
    <div className="editor-list" data-testid="command-editor">
      <EditorIntro title="Typed commands" copy="Stable IDs and enum values are validated by the shared Core." action="Add command" onAction={() => setCommands([...node.commands, createCommand(node.commands)])} />
      {node.commands.map((command, index) => (
        <div className="editor-card" key={command.id}>
          <div className="editor-card__header">
            <span>#{index + 1}</span><code>{command.id.slice(0, 8)}</code>
            <RowActions index={index} count={node.commands.length} onMove={(to) => setCommands(move(node.commands, index, to))} onDelete={() => setCommands(node.commands.filter((item) => item.id !== command.id))} />
          </div>
          <div className="form-grid">
            <Field label="IEC name"><TextInput value={command.name} onChange={(event) => patchCommand(node, command.id, update, (draft) => { draft.name = event.target.value; })} /></Field>
            <Field label="Display name"><TextInput value={command.displayName} onChange={(event) => patchCommand(node, command.id, update, (draft) => { draft.displayName = event.target.value; })} /></Field>
            <Field label="Enum value"><NumberInput min={0} value={command.enumValue} onChange={(event) => patchCommand(node, command.id, update, (draft) => { draft.enumValue = Number(event.target.value); })} /></Field>
            <Field label="ETAB mapping"><SelectInput value={command.etabCommand} onChange={(event) => patchCommand(node, command.id, update, (draft) => { draft.etabCommand = event.target.value as EtabCommand["etabCommand"]; })}>{["NoAction", "Reset", "Start", "Homing", "Stop", "Abort", "Clear", "User"].map((value) => <option key={value}>{value}</option>)}</SelectInput></Field>
            <Field label="Description" wide><TextArea rows={2} value={command.description ?? ""} onChange={(event) => patchCommand(node, command.id, update, (draft) => setOptional(draft, "description", event.target.value))} /></Field>
          </div>
        </div>
      ))}
      {node.commands.length === 0 && <EmptyList>No commands defined.</EmptyList>}
    </div>
  );
}

function PayloadEditor({
  title,
  fields,
  stem,
  update,
}: {
  title: string;
  fields: EtabField[];
  stem: string;
  update: (fields: EtabField[]) => void;
}) {
  const patch = (fieldId: string, mutation: (field: EtabField) => void) => {
    const next = structuredClone(fields);
    const field = next.find((item) => item.id === fieldId);
    if (field) mutation(field);
    update(next);
  };
  return (
    <div className="editor-list" data-testid={`${stem}-editor`}>
      <EditorIntro title={title} copy="Field order is preserved because it is PLC-semantic." action="Add field" onAction={() => update([...fields, createField(fields, stem)])} />
      {fields.map((field, index) => (
        <div className="editor-card" key={field.id}>
          <div className="editor-card__header">
            <span>#{index + 1}</span><code>{field.id.slice(0, 8)}</code>
            <RowActions index={index} count={fields.length} onMove={(to) => update(move(fields, index, to))} onDelete={() => update(fields.filter((item) => item.id !== field.id))} />
          </div>
          <div className="form-grid">
            <Field label="IEC name"><TextInput value={field.name} onChange={(event) => patch(field.id, (draft) => { draft.name = event.target.value; })} /></Field>
            <Field label="TwinCAT data type"><TextInput value={field.dataType} onChange={(event) => patch(field.id, (draft) => { draft.dataType = event.target.value; })} /></Field>
            <Field label="Default value"><TextInput value={field.defaultValue ?? ""} onChange={(event) => patch(field.id, (draft) => setOptional(draft, "defaultValue", event.target.value))} /></Field>
            <div className="field"><Toggle label="Array" checked={Boolean(field.arrayDimensions?.length)} onChange={(checked) => patch(field.id, (draft) => { if (checked) draft.arrayDimensions = [{ lower: 1, upper: 1 }]; else delete draft.arrayDimensions; })} /></div>
            {field.arrayDimensions?.map((dimension, dimensionIndex) => (
              <div className="array-row field--wide" key={dimensionIndex}>
                <span>Dimension {dimensionIndex + 1}</span>
                <NumberInput value={dimension.lower} onChange={(event) => patch(field.id, (draft) => { draft.arrayDimensions![dimensionIndex].lower = Number(event.target.value); })} />
                <span>to</span>
                <NumberInput value={dimension.upper} onChange={(event) => patch(field.id, (draft) => { draft.arrayDimensions![dimensionIndex].upper = Number(event.target.value); })} />
                {field.arrayDimensions!.length < 3 && <button className="mini-button" onClick={() => patch(field.id, (draft) => { draft.arrayDimensions!.push({ lower: 1, upper: 1 }); })}>+ dimension</button>}
                <IconButton label="Remove dimension" onClick={() => patch(field.id, (draft) => { draft.arrayDimensions!.splice(dimensionIndex, 1); if (draft.arrayDimensions!.length === 0) delete draft.arrayDimensions; })}>×</IconButton>
              </div>
            ))}
            <Field label="Description" wide><TextArea rows={2} value={field.description ?? ""} onChange={(event) => patch(field.id, (draft) => setOptional(draft, "description", event.target.value))} /></Field>
          </div>
        </div>
      ))}
      {fields.length === 0 && <EmptyList>No payload fields defined.</EmptyList>}
    </div>
  );
}

function RelationEditor({ document, node, updateDocument }: { document: EtabProjectDocument; node: EtabNode; updateDocument: (mutation: DocumentMutation) => void }) {
  const [kind, setKind] = useState<RelationKind>("contains");
  const [targetId, setTargetId] = useState("");
  const [label, setLabel] = useState("");
  const definitions = getSourceRelationDefinitions(node);
  const targets = getEligibleTargets(document, node.id, kind);

  useEffect(() => {
    if (!definitions.some((definition) => definition.kind === kind) && definitions[0]) {
      setKind(definitions[0].kind);
    }
  }, [definitions, kind]);

  useEffect(() => {
    if (!targets.some((target) => target.id === targetId)) {
      setTargetId(targets[0]?.id ?? "");
    }
  }, [targetId, targets]);

  const byId = new Map(document.nodes.map((item) => [item.id, item]));
  const related = document.relations.filter((relation) => relation.sourceNodeId === node.id || relation.targetNodeId === node.id);
  return (
    <div className="editor-list" data-testid="relation-editor">
      {definitions.length > 0 ? (
        <div className="relation-create">
          <div>
            <h3>New relationship</h3>
            <p><strong>{node.displayName}</strong> is the source. Only valid types and targets are offered.</p>
          </div>
          <Field label="Relationship type" hint={getRelationDefinition(kind).description}>
            <SelectInput value={kind} onChange={(event) => setKind(event.target.value as RelationKind)}>
              {definitions.map((definition) => (
                <option key={definition.kind} value={definition.kind}>{definition.label} ({definition.kind})</option>
              ))}
            </SelectInput>
          </Field>
          <Field label="Target">
            <SelectInput value={targetId} onChange={(event) => setTargetId(event.target.value)} disabled={targets.length === 0}>
              {targets.length === 0 && <option value="">No valid target available</option>}
              {targets.map((target) => <option key={target.id} value={target.id}>{target.displayName} · {nodeKindLabels[target.kind]}</option>)}
            </SelectInput>
          </Field>
          <Field label="Optional line label"><TextInput value={label} placeholder={getRelationDefinition(kind).label} onChange={(event) => setLabel(event.target.value)} /></Field>
          <button className="button button--primary" disabled={!targetId} onClick={() => {
            updateDocument((draft) => {
              draft.relations.push({ id: crypto.randomUUID().toLowerCase(), kind, sourceNodeId: node.id, targetNodeId: targetId, ...(label.trim() ? { label: label.trim() } : {}) });
              if (draft.project.generation.relationWiring) {
                draft.nodes.filter((item) => item.id === node.id || item.id === targetId).forEach((item) => { item.generate.instance = true; });
              }
            });
            setLabel("");
          }}>Add relationship</button>
        </div>
      ) : (
        <EmptyList>{nodeKindLabels[node.kind]} nodes can be relationship targets, but not sources.</EmptyList>
      )}
      {related.map((relation) => (
        <div className="relation-card" key={relation.id}>
          <span className={`relation-chip relation-chip--${relation.kind}`} title={relation.kind}>{getRelationDefinition(relation.kind).label}</span>
          <div>
            <strong>{byId.get(relation.sourceNodeId)?.displayName ?? "Unknown"}</strong>
            <span>→</span>
            <strong>{byId.get(relation.targetNodeId)?.displayName ?? "Unknown"}</strong>
            <small>{relation.label || relation.kind}</small>
          </div>
          <IconButton label="Delete relationship" danger onClick={() => updateDocument((draft) => { draft.relations = draft.relations.filter((item) => item.id !== relation.id); })}>×</IconButton>
        </div>
      ))}
      {related.length === 0 && <EmptyList>No relationships touch this node.</EmptyList>}
    </div>
  );
}

function NodeSettings({ node, update }: { node: EtabNode; update: (mutation: NodeMutation) => void }) {
  return (
    <div className="settings-stack">
      <section className="settings-card">
        <h3>Generated artifacts</h3>
        {(["commandEnum", "requestType", "statusType", "baseFunctionBlock", "instance"] as const).map((key) => (
          <Toggle key={key} label={splitCamel(key)} checked={node.generate[key]} disabled={key === "baseFunctionBlock" && node.kind !== "applicationUnit"} onChange={(checked) => update((draft) => { draft.generate[key] = checked; if (key === "instance" && !checked) { delete draft.generate.instanceType; delete draft.generate.relationStatusMember; draft.generate.callInProgram = false; } })} />
        ))}
        {node.generate.instance && <Field label="Instance type"><TextInput value={node.generate.instanceType ?? ""} onChange={(event) => update((draft) => { setOptional(draft.generate, "instanceType", event.target.value); if (!event.target.value.trim()) delete draft.generate.relationStatusMember; })} /></Field>}
        {node.generate.instance && node.generate.instanceType && (node.kind === "recipeManager" || node.kind === "machineLink") && <Field label="Relation status output" hint="Leave empty for stStatus. Custom wrapper FBs may expose another IEC member."><TextInput placeholder="stStatus" value={node.generate.relationStatusMember ?? ""} onChange={(event) => update((draft) => setOptional(draft.generate, "relationStatusMember", event.target.value))} /></Field>}
        {node.generate.instance && <Toggle label="Run cyclically in generated runtime" checked={node.generate.callInProgram ?? false} onChange={(checked) => update((draft) => { draft.generate.callInProgram = checked; })} />}
        {node.generate.instance && <p className="settings-note">Leave the type empty to use the generated base FB or the ETAB library type.</p>}
      </section>
      {node.applicationUnit && <section className="settings-card form-grid"><SectionTitle>Application Unit</SectionTitle><Field label="Start mode"><TextInput value={node.applicationUnit.startMode} onChange={(event) => update((draft) => { draft.applicationUnit!.startMode = event.target.value; })} /></Field><Field label="Homing mode"><TextInput value={node.applicationUnit.homingMode} onChange={(event) => update((draft) => { draft.applicationUnit!.homingMode = event.target.value; })} /></Field><Field label="Stop mode"><TextInput value={node.applicationUnit.stopMode} onChange={(event) => update((draft) => { draft.applicationUnit!.stopMode = event.target.value; })} /></Field><Field label="Command start state"><NumberInput min={1} value={node.applicationUnit.command.startState} onChange={(event) => update((draft) => { draft.applicationUnit!.command.startState = Number(event.target.value); })} /></Field><div className="field field--wide"><Toggle label="Keep remote control" checked={node.applicationUnit.keepRemoteControl} onChange={(checked) => update((draft) => { draft.applicationUnit!.keepRemoteControl = checked; })} /><Toggle label="Set machine error on command error" checked={node.applicationUnit.setMachineErrorOnCommandError} onChange={(checked) => update((draft) => { draft.applicationUnit!.setMachineErrorOnCommandError = checked; })} /><Toggle label="Reset command error on start" checked={node.applicationUnit.command.resetErrorOnStart} onChange={(checked) => update((draft) => { draft.applicationUnit!.command.resetErrorOnStart = checked; })} /></div></section>}
      {node.commandUnit && <section className="settings-card form-grid"><SectionTitle>Command Unit</SectionTitle><Field label="Start state"><NumberInput min={1} value={node.commandUnit.startState} onChange={(event) => update((draft) => { draft.commandUnit!.startState = Number(event.target.value); })} /></Field><div className="field"><Toggle label="Reset error on start" checked={node.commandUnit.resetErrorOnStart} onChange={(checked) => update((draft) => { draft.commandUnit!.resetErrorOnStart = checked; })} /></div></section>}
      {node.recipeManager && <section className="settings-card form-grid"><SectionTitle>Recipe Manager</SectionTitle><Field label="Data type"><TextInput value={node.recipeManager.dataType} onChange={(event) => update((draft) => { draft.recipeManager!.dataType = event.target.value; })} /></Field><Field label="File name"><TextInput value={node.recipeManager.fileName} onChange={(event) => update((draft) => { draft.recipeManager!.fileName = event.target.value; })} /></Field><Field label="File path" wide><TextInput value={node.recipeManager.filePath} onChange={(event) => update((draft) => { draft.recipeManager!.filePath = event.target.value; })} /></Field><Field label="XPath" wide><TextInput value={node.recipeManager.xPath} onChange={(event) => update((draft) => { draft.recipeManager!.xPath = event.target.value; })} /></Field><div className="field field--wide"><Toggle label="Enable auto-save" checked={node.recipeManager.enableAutoSave ?? false} onChange={(checked) => update((draft) => { draft.recipeManager!.enableAutoSave = checked; })} /><Toggle label="Enable backup file" checked={node.recipeManager.enableBackupFile ?? false} onChange={(checked) => update((draft) => { draft.recipeManager!.enableBackupFile = checked; })} /><Toggle label="Require external validation" checked={node.recipeManager.requireExternalValidation ?? false} onChange={(checked) => update((draft) => { draft.recipeManager!.requireExternalValidation = checked; })} /></div></section>}
      {node.machineLink && <section className="settings-card form-grid"><SectionTitle>Machine Link</SectionTitle><Field label="Bridge type"><SelectInput value={node.machineLink.bridgeType} onChange={(event) => update((draft) => { draft.machineLink!.bridgeType = event.target.value as "GenericBridge" | "EL6695" | "EL6692" | "ExternalBridge"; })}>{["GenericBridge", "EL6695", "EL6692", "ExternalBridge"].map((value) => <option key={value}>{value}</option>)}</SelectInput></Field><Field label="Watchdog"><TextInput value={node.machineLink.watchdogTime} onChange={(event) => update((draft) => { draft.machineLink!.watchdogTime = event.target.value; })} /></Field><div className="field field--wide"><Toggle label="Primary role" checked={node.machineLink.isPrimary} onChange={(checked) => update((draft) => { draft.machineLink!.isPrimary = checked; })} /><Toggle label="Primary wins tie" checked={node.machineLink.primaryWinsTie} onChange={(checked) => update((draft) => { draft.machineLink!.primaryWinsTie = checked; })} /><Toggle label="Allow token without partner" checked={node.machineLink.allowTokenWithoutPartnerAlive} onChange={(checked) => update((draft) => { draft.machineLink!.allowTokenWithoutPartnerAlive = checked; })} /><Toggle label="Clear Tx when disabled" checked={node.machineLink.clearTxWhenDisabled} onChange={(checked) => update((draft) => { draft.machineLink!.clearTxWhenDisabled = checked; })} /></div></section>}
      {node.mtp && <section className="settings-card form-grid"><SectionTitle>MTP preparation</SectionTitle><div className="field"><Toggle label="Expose as service" checked={node.mtp.exposed} onChange={(checked) => update((draft) => { draft.mtp!.exposed = checked; })} /></div><Field label="Service name"><TextInput value={node.mtp.serviceName ?? ""} onChange={(event) => update((draft) => setOptional(draft.mtp!, "serviceName", event.target.value))} /></Field><p className="settings-note field--wide">Procedure editing remains reserved for the optional MTP phase. Existing procedures are preserved during every edit and save.</p></section>}
    </div>
  );
}

function EditorIntro({ title, copy, action, onAction }: { title: string; copy: string; action: string; onAction: () => void }) { return <div className="editor-intro"><div><h3>{title}</h3><p>{copy}</p></div><button className="button button--secondary" onClick={onAction}>+ {action}</button></div>; }
function EmptyList({ children }: { children: React.ReactNode }) { return <div className="empty-list">{children}</div>; }
function SectionTitle({ children }: { children: React.ReactNode }) { return <h3 className="form-section-title field--wide">{children}</h3>; }
function RowActions({ index, count, onMove, onDelete }: { index: number; count: number; onMove: (to: number) => void; onDelete: () => void }) { return <div className="row-actions"><IconButton label="Move up" disabled={index === 0} onClick={() => onMove(index - 1)}>↑</IconButton><IconButton label="Move down" disabled={index === count - 1} onClick={() => onMove(index + 1)}>↓</IconButton><IconButton label="Delete" danger onClick={onDelete}>×</IconButton></div>; }
function move<T>(items: T[], from: number, to: number): T[] { const next = [...items]; const [item] = next.splice(from, 1); next.splice(to, 0, item); return next; }
function patchCommand(node: EtabNode, id: string, update: (mutation: NodeMutation) => void, mutation: (command: EtabCommand) => void) { update((draft) => { const command = draft.commands.find((item) => item.id === id); if (command) mutation(command); }); }
function setOptional<T extends object, K extends keyof T>(target: T, key: K, value: string) { if (value.trim()) target[key] = value as T[K]; else delete target[key]; }
function splitCamel(value: string) { return value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (letter) => letter.toUpperCase()); }
