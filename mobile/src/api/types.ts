/**
 * Wire types of the Community SOS API (backend/src, docs/03*.md).
 * JSON is camelCase, enums are strings, and null fields are omitted, so optional = may be absent.
 * Timestamps are ISO-8601 UTC strings.
 */

export type UserRole = 'User' | 'SubAdmin' | 'MasterAdmin';
export type UserStatus = 'Active' | 'Suspended' | 'Deleted';
export type DevicePlatform = 'Android' | 'Ios';
export type InvitationStatus = 'Active' | 'Revoked' | 'Expired' | 'Exhausted';
export type ConsentType = 'TermsAndPrivacy' | 'LocationSharing' | 'HealthData';

export interface EmergencyNumber {
  label: string;
  number: string;
}

export interface AppInfo {
  policyVersion: string;
  minimumAppVersion: { android: string; ios: string };
  emergencyNumbers: EmergencyNumber[];
  grievanceContactEmail?: string;
}

export interface SessionUser {
  id: string;
  role: UserRole;
  fullName?: string;
  profileCompleted: boolean;
  policyAcceptanceRequired: boolean;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  isNewUser: boolean;
  user: SessionUser;
}

export interface LoginConsent {
  policyVersion: string;
  confirmedAdult: boolean;
}

export interface LoginRequest {
  firebaseIdToken: string;
  inviteCode?: string;
  consent?: LoginConsent;
  deviceId?: string;
}

export interface AccessTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface ConsentState {
  termsAndPrivacy: boolean;
  locationSharing: boolean;
  healthData: boolean;
}

export interface ReferrerSummary {
  id: string;
  fullName?: string;
  role: UserRole;
}

export interface Profile {
  id: string;
  phoneNumber: string;
  role: UserRole;
  fullName?: string;
  email?: string;
  profilePhotoUrl?: string;
  bloodGroup?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
  alertsEnabled: boolean;
  acceptedPolicyVersion?: string;
  referredBy?: ReferrerSummary;
  consents: ConsentState;
  createdAt: string;
}

export interface UpdateProfileRequest {
  fullName: string;
  email?: string;
  bloodGroup?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
}

export interface InvitationPreview {
  code: string;
  status: InvitationStatus;
  inviteeRole: UserRole;
  invitedBy?: { fullName?: string; role: UserRole };
  expiresAt?: string;
}

/** Push registration by Firebase Installation ID (FCM registration tokens are deprecated; the API still accepts them). */
export interface RegisterDeviceRequest {
  installationId: string;
  platform: DevicePlatform;
  appVersion?: string;
  deviceModel?: string;
}

// ---- SOS (docs/03d-sos.md) ------------------------------------------------------------------

export type SosCategory = 'Medical' | 'Fire' | 'Accident' | 'Crime' | 'Other';
export type SosStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Escalated' | 'Resolved' | 'Expired';
export type SosViewerRole = 'Requester' | 'Admin' | 'Recipient';

export const SOS_CATEGORIES: readonly SosCategory[] = ['Medical', 'Fire', 'Accident', 'Crime', 'Other'];
/** Statuses in which an SOS is still open (one open SOS per person). */
export const OPEN_SOS_STATUSES: readonly SosStatus[] = ['Pending', 'Escalated', 'Approved'];
export const MAX_SOS_PHOTOS = 5;
export const MAX_SOS_DESCRIPTION = 1000;

export interface CreateSosRequest {
  clientRequestId: string;
  category: SosCategory;
  description?: string;
  latitude: number;
  longitude: number;
  accuracyMeters?: number;
  capturedAt: string;
}

export interface SosLocation {
  latitude: number;
  longitude: number;
  accuracyMeters?: number;
  mapsUrl: string;
}

export interface SosRequesterInfo {
  firstName?: string;
  id?: string;
  fullName?: string;
  phoneNumber?: string;
  bloodGroup?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
}

export interface SosPhoto {
  id: string;
  url: string;
  width: number;
  height: number;
  uploadedAt: string;
}

export interface SosTimelineEntry {
  from?: SosStatus;
  to: SosStatus;
  remarks?: string;
  changedAt: string;
  bySystem: boolean;
}

export interface SosDetail {
  id: string;
  referenceNo: string;
  category: SosCategory;
  description?: string;
  location: SosLocation;
  address?: string;
  status: SosStatus;
  isAutoApproved: boolean;
  createdAt: string;
  reviewedAt?: string;
  reviewRemarks?: string;
  closedAt?: string;
  recipientCount?: number;
  viewerRole: SosViewerRole;
  requester: SosRequesterInfo;
  photos: SosPhoto[];
  timeline?: SosTimelineEntry[];
  /** Recipient view only. */
  myAlert?: SosMyAlert;
  /** Requester and admin views, once anyone was alerted. */
  responders?: SosResponders;
}

// ---- Neighbour alerts (Module 5) -------------------------------------------------------------

export type ResponseType = 'None' | 'CanHelp' | 'OnTheWay' | 'Arrived' | 'CannotHelp';
/** The choices offered to an alerted neighbour, in display order. */
export const RESPONSE_CHOICES: readonly Exclude<ResponseType, 'None'>[] = ['OnTheWay', 'CanHelp', 'Arrived', 'CannotHelp'];
export const MAX_RESPONSE_NOTE = 300;

export interface SosMyAlert {
  distanceMeters: number;
  alertedAt: string;
  acknowledgedAt?: string;
  response: ResponseType;
  note?: string;
  respondedAt?: string;
}

export interface SosResponder {
  firstName?: string;
  response: ResponseType;
  distanceMeters: number;
  respondedAt: string;
  note?: string;
}

export interface SosResponders {
  alerted: number;
  acknowledged: number;
  canHelp: number;
  onTheWay: number;
  arrived: number;
  cannotHelp: number;
  helping: SosResponder[];
}

export interface SosAlertItem {
  sosId: string;
  referenceNo: string;
  category: SosCategory;
  status: SosStatus;
  distanceMeters: number;
  /** Only while the SOS still needs help. */
  address?: string;
  alertedAt: string;
  acknowledgedAt?: string;
  response: ResponseType;
  respondedAt?: string;
}

export interface SosSummary {
  id: string;
  referenceNo: string;
  category: SosCategory;
  status: SosStatus;
  address?: string;
  isAutoApproved: boolean;
  photoCount: number;
  createdAt: string;
  closedAt?: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** POST /me/location-key. */
export interface LocationKeyResponse {
  key: string;
}

export interface UpdateLocationRequest {
  latitude: number;
  longitude: number;
  accuracyMeters?: number;
  capturedAt: string;
}

// ---- Admin (docs/03c-invitations-admin.md, docs/03e-admin-review.md) -------------------------

export const MAX_REVIEW_REMARKS = 500;
export const MAX_SUSPEND_REASON = 300;
export const MAX_INVITATION_LABEL = 100;
export const MAX_INVITATION_USES = 10_000;

export interface UserSummary {
  id: string;
  fullName?: string;
  role: UserRole;
  status: UserStatus;
}

export interface AdminSosListItem {
  id: string;
  referenceNo: string;
  category: SosCategory;
  status: SosStatus;
  isAutoApproved: boolean;
  requester: UserSummary;
  requesterPhone?: string;
  assignedAdmin: UserSummary;
  address?: string;
  latitude: number;
  longitude: number;
  photoCount: number;
  recipientCount: number;
  createdAt: string;
  escalatedAt?: string;
  reviewedAt?: string;
}

export interface AdminSosListQuery {
  awaitingReview?: boolean;
  status?: SosStatus;
  from?: string;
  to?: string;
  assignedAdminId?: string;
  page?: number;
  pageSize?: number;
}

export interface DashboardStats {
  totalUsers: number;
  subAdmins: number;
  normalUsers: number;
  referredUsers: number;
  newUsersLast7Days: number;
  totalSos: number;
  pendingSos: number;
  escalatedSos: number;
  approvedSos: number;
  autoApprovedSos: number;
  rejectedSos: number;
  activeSos: number;
  resolvedSos: number;
  cancelledSos: number;
  expiredSos: number;
  sosLast24Hours: number;
  avgReviewSeconds?: number;
}

export interface MapBounds {
  minLatitude: number;
  maxLatitude: number;
  minLongitude: number;
  maxLongitude: number;
}

export interface SosMapPoint {
  id: string;
  referenceNo: string;
  category: SosCategory;
  status: SosStatus;
  latitude: number;
  longitude: number;
  createdAt: string;
}

export interface SosMapResponse {
  points: SosMapPoint[];
  truncated: boolean;
}

export interface CreateInvitationRequest {
  label?: string;
  maxUses?: number;
  expiresAt?: string;
}

export interface Invitation {
  id: string;
  code: string;
  link: string;
  inviteeRole: UserRole;
  label?: string;
  maxUses?: number;
  usedCount: number;
  expiresAt?: string;
  status: InvitationStatus;
  createdAt: string;
  revokedAt?: string;
}

export interface AdminUserListItem {
  id: string;
  fullName?: string;
  phoneNumber?: string;
  role: UserRole;
  status: UserStatus;
  referredBy?: UserSummary;
  referredUsersCount: number;
  alertsEnabled: boolean;
  createdAt: string;
  lastLoginAt?: string;
}

export interface AdminUserQuery {
  role?: UserRole;
  status?: UserStatus;
  search?: string;
  referredBy?: string;
  page?: number;
  pageSize?: number;
}

export interface AdminUserDetail {
  user: AdminUserListItem;
  email?: string;
  acceptedPolicyVersion?: string;
  referral?: { invitationId: string; invitationLabel?: string; registeredAt: string };
  sosRaised: number;
  activeInvitations: number;
}

export interface ReferralEntry {
  id: number;
  referredUser: UserSummary;
  referrer: UserSummary;
  invitationId: string;
  invitationLabel?: string;
  assignedRole: UserRole;
  registeredAt: string;
}

export interface SubAdminNode {
  subAdmin: UserSummary;
  phoneNumber?: string;
  referredUsers: number;
  activeUsers: number;
  activeInvitations: number;
  joinedAt: string;
}

export interface ReferralTree {
  root: UserSummary;
  totalSubAdmins: number;
  totalUsers: number;
  subAdmins: SubAdminNode[];
}

/** RFC 7807 problem details as returned by the API (docs/03a-api-foundation.md). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  traceId?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}
