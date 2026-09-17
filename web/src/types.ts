export type Page = {path:string;content:string;version:string;updated:string};
export type MemorySelection={id:string;version:string};
export type MemoryEntry=MemorySelection&{statement:string;source:{path:string;version:string;quote:string}|null;updated:string;forgotten:boolean};
export type MemoryView={entry:MemoryEntry;sourceStatus:string};
export type Provider = {kind:string;model:string;reasoning:string;endpoint?:string;credentialId?:string};
export type ResearchAvailability = {enabled:boolean;backend:string;status:string;summary:string;developmentOnly:boolean};
export type ResearchRecoveryReview = {digest:string;version:number;checkpoint?:string;canRestore:boolean;summary:string;checkedAt:string;expires:string;workerStopped:boolean};
export type ResearchState = {recovery?:ResearchRecoveryReview;phase:string;message:string;workerRetained:boolean;failureCode?:string;review?:{approvalId:string;artifact:string;sha256:string;readAt:string}};
export type NativeProposalReview = {id:string;path:string;artifact:string;content:string;status:string;contentHash:string;assessment:{passed:boolean;checks:string[];problems:string[];unverified:string[]}};
export type ArtifactCheck = {approvalId:string;artifact:string;expectedSha256:string;observedSha256?:string;status:string;checkedAt:string;failureType?:string};
export type ArtifactImport = {id:string;path:string;artifact:string;status:string;sha256?:string;requested:string;capturedAt?:string;approvalId?:string};
export type ExecutionCommand={id:string;kind:string;status:string;message?:string;requested:string};
export type ConnectedTool={connectorId:string;connectorName:string;remoteName:string;modelName:string;description:string;effect:string;connectionVersion:string};
export type DelegationSchedule={kind:'once'|'weekdays'|'interval';atUtc?:string|null;timeZone:string;localTime?:string|null;intervalMinutes?:number|null};
export type DelegatedAction={kind:'reminder'|'email'|'brief'|'inbox-watch';target:string;payload:Record<string,unknown>;requiresModel:boolean};
export type DelegationJob={id:string;version:number;kind:'reminder'|'email'|'brief'|'inbox-watch';title:string;schedule:DelegationSchedule;action:DelegatedAction;state:string;requestedAt:string;created:string;updated:string;nextRunUtc?:string|null;lastSummary?:string|null;cancellationRequested:boolean};
export type DelegationOccurrence={id:string;version:number;jobId:string;sequence:number;dueUtc:string;state:string;dispatchState:string;completedAt?:string|null;summary?:string|null;providerId?:string|null;providerEvidence?:Record<string,unknown>|null;actionSucceeded:boolean;notificationStatus:string;notificationError?:string|null;readAt?:string|null};
export type Run = {connectedTools?:ConnectedTool[];conversationRetry?:{rootId:string;sourceId:string;operationId:string};artifactContext?:{selected?:{id:string};localDate:string};suggestIdeas?:boolean;uploadIds?:string[];background?:boolean;artifactResult?:ArtifactResult;executionCommands?:ExecutionCommand[];version:number;capabilities?:PublicCapability[];artifactImports?:ArtifactImport[];artifactChecks?:ArtifactCheck[];nativeProposals?:NativeProposalReview[];preparedContext?:{memories?:MemoryEntry[]};research?:ResearchState;id:string;state:string;summary:string;created:string;updated:string;draftText?:string;chargedTokens?:number;reservedTokens?:number;tokenAccounting?:string;question?:{id:string;text:string;choices:string[];answer?:string};execution?:{backend:string;sandboxId:string;sessionKey:string;runtimeVersion:string;runtimeRunId?:string};goal:{kind?:string;web?:PublicWebScope;objective:string;readScope:string[];provider:Provider;limits:Record<string,number>;criteria:{description:string;status:string}[]};approval?:{id:string;digest:string;action:{name:string;path:string;content:string};expires:string;decision:string};evidence:{path:string;hash:string;content:string}[];modelCalls:number;toolCalls:number;repairs:number;inputTokens?:number;outputTokens?:number;validation?:{checks:string[];unverified:string[]};outputPath?:string};
export type UploadFile={id:string;name:string;mediaType:string;bytes:number;sha256:string;created:string;version:string;archived:boolean};
export type State = {delegations?:DelegationJob[];delegationOccurrences?:DelegationOccurrence[];hostMustRemainAwake?:boolean;uploads?:UploadFile[];artifacts?:AppSummary[];search?:SearchSummary;feeds?:FeedState;library:LibraryItem[];research?:ResearchAvailability;memories?:MemoryView[];retainedResearchWorkspaces?:boolean;runs:Run[];pages:Page[];chats:{id:string;role:string;content:string}[];provider:Provider;writes:string;phoneOrigin?:string};

export type AppField={key:string;label:string;kind:'text'|'number'|'date'|'checkbox'|'select';unit?:string|null;options?:string[]|null};
export type AppDefinition={title:string;description:string;fields:AppField[];summaries:string[];dateField?:string|null;page?:{html:string;css:string;javaScript:string}|null};
export type AppEntry={id:string;values:Record<string,string|number|boolean|null>};
export type ArtifactApp={id:string;definition:AppDefinition;entries:AppEntry[];version:string;created:string;updated:string;archived:boolean};
export type AppSummary={id:string;title:string;description:string;version:string;entryCount:number;archived:boolean};
export type ArtifactResult={id:string;version:string;description:string;changed:boolean;deleted?:boolean};
export type AppRevision={id:string;version:string;description:string;source:string;at:string;title:string;entryCount:number};

export type TrackingPlan={section:"tracked"|"daily"|"weekly"|"goals";cadence:"none"|"daily"|"weekly";current?:number|null;target?:number|null;unit?:string|null;nextStep?:string|null;nextCheckIn?:string|null;lastCheckIn?:string|null};
export type LibraryItem={tracking?:TrackingPlan|null;category?:string|null;prompt?:string|null;id:string;kind:"todo"|"idea"|"feed";title:string;content:string;status:string;url:string|null;due:string|null;version:string;created:string;updated:string};

export type SearchBudget={version:string;monthlyLimit:number;used:number;remaining:number;month:string;resets:string};
export type SearchSummary={temporaryConfigured?:boolean;provider:string;configured:boolean;credentialId:string|null;maxQueries:number;providerVerified:boolean;budget?:SearchBudget};
export type PublicSearchGrant={provider:string;credentialId:string;maxQueries:number;openResults:boolean};
export type PublicWebScope={hosts:string[];maxFetches:number;search?:PublicSearchGrant};
export type PublicCapability={operationId:string;name:string;authority:string;recorded:string;isError:boolean;result:{source?:{url:string;title:string;retrieved:string;truncated:boolean};query?:string;provider?:string;status?:string;error?:string;outcomeUnknown?:boolean;httpStatus?:number;results?:{url:string;title:string;description:string}[]}};

export type FeedSubscription={id:string;url:string;title:string;paused:boolean;version:string;created:string;nextRefresh:string;lastAttempt?:string;lastChecked?:string;error?:string;failures:number;truncated:boolean};
export type FeedEngagement={opened?:string;saved?:string;discussed?:string;preference:number;preferred?:string};
export type FeedEntry={id:string;subscriptionId:string;key:string;title:string;summary:string;url?:string;published?:string;received:string;read:boolean;version:string;savedItemId?:string;engagement?:FeedEngagement};
export type FeedState={subscriptions:FeedSubscription[];entries:FeedEntry[];revision:string;preferences?:{enabled:boolean;version:string}};
export type FeedPreview={url:string;feed?:{title:string;entries:{title:string}[];truncated:boolean};candidates?:{title:string;url:string}[];error?:string};
