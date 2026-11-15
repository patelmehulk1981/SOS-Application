import { anonymousClient, api } from './index';
import type {
  AccessTokenResponse,
  AdminSosListItem,
  AdminSosListQuery,
  AdminUserDetail,
  AdminUserListItem,
  AdminUserQuery,
  AppInfo,
  AuthResponse,
  ConsentState,
  ConsentType,
  CreateInvitationRequest,
  CreateSosRequest,
  DashboardStats,
  Invitation,
  InvitationPreview,
  InvitationStatus,
  LocationKeyResponse,
  LoginRequest,
  MapBounds,
  ReferralEntry,
  ReferralTree,
  ResponseType,
  SosAlertItem,
  SosMapResponse,
  PagedResult,
  Profile,
  RegisterDeviceRequest,
  SosDetail,
  SosStatus,
  SosSummary,
  UpdateLocationRequest,
  UpdateProfileRequest,
} from './types';

/** Typed wrappers for the API endpoints. Paths and contracts: docs/03a–03d. */

/**
 * Background location reports. Sent with the location key, never with the session: background work
 * must not refresh (rotate) the session, see docs/06-background-location.md.
 */
export const backgroundLocationApi = {
  report: (locationKey: string, request: UpdateLocationRequest) =>
    anonymousClient.put<void>('/location/background', request, {
      auth: false,
      headers: { 'X-Location-Key': locationKey },
      timeoutMs: 15_000,
    }),
};

export const appInfoApi = {
  get: () => api.get<AppInfo>('/app-info', { auth: false }),
};

export const authApi = {
  login: (request: LoginRequest) => api.post<AuthResponse>('/auth/login', request, { auth: false }),
  /** `installationId`: this phone's push registration, deactivated so it stops receiving alerts. */
  logout: (refreshToken: string, installationId?: string) => api.post<void>('/auth/logout', { refreshToken, installationId }),
};

export const invitationsApi = {
  preview: (code: string) =>
    api.get<InvitationPreview>(`/invitations/${encodeURIComponent(code)}`, { auth: false }),
  // Admin only (docs/03c).
  create: (request: CreateInvitationRequest) => api.post<Invitation>('/invitations', request),
  list: (params: { status?: InvitationStatus; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<Invitation>>('/invitations', { query: params }),
  revoke: (id: string) => api.delete<void>(`/invitations/${encodeURIComponent(id)}`),
};

/** Admin SOS review, dashboard and map (docs/03e). */
export const adminSosApi = {
  list: (params: AdminSosListQuery = {}) => api.get<PagedResult<AdminSosListItem>>('/admin/sos', { query: { ...params } }),
  approve: (id: string, remarks?: string) =>
    api.post<SosDetail>(`/admin/sos/${encodeURIComponent(id)}/approve`, { remarks }),
  reject: (id: string, remarks: string) =>
    api.post<SosDetail>(`/admin/sos/${encodeURIComponent(id)}/reject`, { remarks }),
  map: (bounds: MapBounds, params: { status?: SosStatus; from?: string; to?: string } = {}, signal?: AbortSignal) =>
    api.get<SosMapResponse>('/admin/sos/map', { query: { ...bounds, ...params }, signal }),
  dashboard: (params: { from?: string; to?: string; adminId?: string } = {}) =>
    api.get<DashboardStats>('/admin/dashboard', { query: params }),
};

/** Admin users and referrals (docs/03c). */
export const adminUsersApi = {
  list: (params: AdminUserQuery = {}) => api.get<PagedResult<AdminUserListItem>>('/admin/users', { query: { ...params } }),
  get: (id: string) => api.get<AdminUserDetail>(`/admin/users/${encodeURIComponent(id)}`),
  setStatus: (id: string, status: 'Active' | 'Suspended', reason?: string) =>
    api.put<AdminUserDetail>(`/admin/users/${encodeURIComponent(id)}/status`, { status, reason }),
  referrals: (params: { adminId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<ReferralEntry>>('/admin/referrals', { query: params }),
  referralTree: () => api.get<ReferralTree>('/admin/referrals/tree'),
};

export const meApi = {
  get: () => api.get<Profile>('/me'),
  updateProfile: (request: UpdateProfileRequest) => api.put<Profile>('/me/profile', request),
  updateConsent: (type: Exclude<ConsentType, 'TermsAndPrivacy'>, granted: boolean) =>
    api.put<ConsentState>('/me/consents', { type, granted }),
  acceptPolicy: (policyVersion: string) =>
    api.post<AccessTokenResponse>('/me/policy-acceptance', { policyVersion, confirmedAdult: true }),
  registerDevice: (request: RegisterDeviceRequest) => api.post<void>('/me/devices', request),
  updatePreferences: (alertsEnabled: boolean) => api.put<Profile>('/me/preferences', { alertsEnabled }),
  updateLocation: (request: UpdateLocationRequest) => api.put<void>('/me/location', request),
  /** Background location for this phone (docs/06): the key goes to secure storage, never to logs. */
  issueLocationKey: (deviceId: string) => api.post<LocationKeyResponse>('/me/location-key', { deviceId }),
  revokeLocationKey: (deviceId: string) => api.post<void>('/me/location-key/revoke', { deviceId }),
  uploadPhoto: (photo: UploadFile) => {
    const form = new FormData();
    appendFile(form, 'photo', photo);
    return api.request<Profile>('/me/photo', { method: 'POST', form });
  },
  removePhoto: () => api.delete<Profile>('/me/photo'),
  /** Full personal-data export (DPDP s.11 / GDPR Art. 15, 20) as parsed JSON. */
  exportData: () => api.get<Record<string, unknown>>('/me/data-export', { timeoutMs: 60_000 }),
  deleteAccount: () => api.delete<void>('/me'),
};

/** A local file for multipart upload (React Native FormData form). */
export interface UploadFile {
  uri: string;
  name: string;
  type: string;
}

/**
 * React Native's FormData streams `{ uri, name, type }` straight from disk; the DOM typings
 * (which win in tsconfig) only know Blob, hence the cast.
 */
function appendFile(form: FormData, field: string, file: UploadFile): void {
  form.append(field, file as unknown as Blob);
}

export const sosApi = {
  create: (request: CreateSosRequest) => api.post<SosDetail>('/sos', request),
  get: (id: string) => api.get<SosDetail>(`/sos/${encodeURIComponent(id)}`),
  mine: (params: { status?: SosStatus; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<SosSummary>>('/sos/mine', { query: params }),
  uploadPhotos: (id: string, photos: UploadFile[]) => {
    const form = new FormData();
    for (const photo of photos) appendFile(form, 'photos', photo);
    return api.request<SosDetail>(`/sos/${encodeURIComponent(id)}/photos`, { method: 'POST', form });
  },
  cancel: (id: string, reason?: string) => api.post<SosDetail>(`/sos/${encodeURIComponent(id)}/cancel`, { reason }),
  resolve: (id: string, remarks?: string) =>
    api.post<SosDetail>(`/sos/${encodeURIComponent(id)}/resolve`, { remarks }),
  // Neighbour side (docs/05-push-alerts.md).
  alerts: (params: { openOnly?: boolean; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<SosAlertItem>>('/sos/alerts', { query: params }),
  acknowledge: (id: string) => api.post<void>(`/sos/${encodeURIComponent(id)}/acknowledge`),
  respond: (id: string, response: Exclude<ResponseType, 'None'>, note?: string) =>
    api.put<SosDetail>(`/sos/${encodeURIComponent(id)}/response`, { response, note }),
};
