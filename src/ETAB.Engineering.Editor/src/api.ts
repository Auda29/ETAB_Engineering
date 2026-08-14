import type {
  EtabProjectDocument,
  ConnectPlcProjectDialogResponse,
  GenerateProjectResponse,
  NewProjectResponse,
  OpenProjectResponse,
  ProjectFileDialogResponse,
  PreviewResponse,
  SaveProjectResponse,
  SessionResponse,
  ValidationResponse,
} from "./model";

interface ApiError { code?: string; message?: string }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json", ...init?.headers },
    ...init,
  });
  if (!response.ok) {
    const error = (await response.json().catch(() => ({}))) as ApiError;
    throw new Error(error.message ?? `${response.status} ${response.statusText}`);
  }
  return response.json() as Promise<T>;
}

export const editorApi = {
  session: (signal?: AbortSignal) => request<SessionResponse>("/api/session", { signal }),
  createNew: () => request<NewProjectResponse>("/api/projects/new", {
    method: "POST",
  }),
  connectPlcProject: () => request<ConnectPlcProjectDialogResponse>("/api/projects/connect-plc", {
    method: "POST",
  }),
  chooseOpenProject: () => request<ProjectFileDialogResponse>("/api/dialogs/open-project", {
    method: "POST",
  }),
  chooseSaveProject: (suggestedFileName: string) =>
    request<ProjectFileDialogResponse>("/api/dialogs/save-project", {
      method: "POST",
      body: JSON.stringify({ suggestedFileName }),
    }),
  open: (path: string) => request<OpenProjectResponse>("/api/projects/open", {
    method: "POST",
    body: JSON.stringify({ path }),
  }),
  save: (path: string, document: EtabProjectDocument) =>
    request<SaveProjectResponse>("/api/projects/save", {
      method: "POST",
      body: JSON.stringify({ path, document }),
    }),
  validate: (document: EtabProjectDocument, signal?: AbortSignal) =>
    request<ValidationResponse>("/api/projects/validate", {
      method: "POST",
      body: JSON.stringify({ document }),
      signal,
    }),
  preview: (
    document: EtabProjectDocument,
    projectPath: string,
    projectRoot: string,
    integrateProject: boolean,
  ) =>
    request<PreviewResponse>("/api/projects/preview", {
      method: "POST",
      body: JSON.stringify({ document, projectPath, projectRoot, integrateProject }),
    }),
  generate: (
    document: EtabProjectDocument,
    projectPath: string,
    projectRoot: string,
    integrateProject: boolean,
    confirmationToken: string,
  ) =>
    request<GenerateProjectResponse>("/api/projects/generate", {
      method: "POST",
      body: JSON.stringify({
        document,
        projectPath,
        projectRoot,
        integrateProject,
        confirmationToken,
        confirmed: true,
      }),
    }),
};
