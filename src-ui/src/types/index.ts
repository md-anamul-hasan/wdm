export interface DownloadRecord {
  Id: string;
  Url: string;
  FileName: string;
  TargetFilePath: string;
  Status: string;
  TotalBytes: number;
  DownloadedBytes: number;
  AddedAt: string;
  CompletedAt?: string;
  Options?: DownloadRequestOptions;
}

export interface DownloadRequestOptions {
  Referer?: string;
  Cookies?: string;
  CookieHost?: string;
  UserAgent?: string;
  Username?: string;
  Password?: string;
  FileName?: string;
  ExpectedSha256?: string;
}

export interface DownloadSegment {
  SegmentId: number;
  StartOffset: number;
  CurrentOffset: number;
  EndOffset?: number;
  State: 'Idle' | 'Connecting' | 'Downloading' | 'Completed' | 'Error';
  SpeedBytesPerSecond: number;
  DownloadedBytes: number;
  TotalBytes: number;
}

export interface DownloadProgress {
  TotalBytes: number;
  DownloadedBytes: number;
  SpeedBytesPerSecond: number;
  EstimatedSecondsRemaining?: number;
  Segments: DownloadSegment[];
  Status: string;
}

export interface AppSettings {
  DefaultDownloadDirectory: string;
  DefaultConnections: number;
  MaxConcurrentDownloads: number;
  DefaultSpeedLimitKb: number;
  IsSpeedLimiterEnabled: boolean;
  AutoOpenFileOnComplete: boolean;
  ConfirmBeforeDelete: boolean;
  EnableDynamicMiddleSplit: boolean;
  ShowStartDownloadDialog: boolean;
  NetworkTimeoutSeconds: number;
  UserAgent: string;
  SchedulerEnabled: boolean;
  SchedulerStartTime: string;
  SchedulerStopTime: string;
  SchedulerShutdownOnComplete: boolean;
  SchedulerStopQueueAtEndTime: boolean;
  IsDarkMode: boolean;
}

export interface GrabMediaItem {
  Url: string;
  FileName: string;
  Category: string;
  Extension: string;
  IsSelected: boolean;
}

export interface ExternalDownloadRequest {
  Urls: string[];
  Referer?: string;
  Cookies?: string;
  CookieHost?: string;
  UserAgent?: string;
  FileName?: string;
  DestinationDirectory?: string;
  Sha256?: string;
  Silent?: boolean;
  QueueOnly?: boolean;
  ActivateOnly?: boolean;
  Connections?: number;
}

export type CategoryFilter = 
  | 'All' 
  | 'Video' 
  | 'Music' 
  | 'Compressed' 
  | 'Documents' 
  | 'Programs' 
  | 'Images' 
  | 'Other';

export type StatusFilter = 
  | 'All' 
  | 'InProgress' 
  | 'Completed' 
  | 'Queued' 
  | 'Paused' 
  | 'Error';

