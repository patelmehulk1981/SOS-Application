# Module 3c: Invitations, referrals and admin user management

Base URL `/api/v1`. Conventions as in [03a](03a-api-foundation.md) (camelCase, enum strings, nulls omitted, problem+json errors).

## Who can do what

| Action | Master Admin | Sub-Admin | User |
|---|---|---|---|
| Create invitation link | ✅ (joins as **SubAdmin**) | ✅ (joins as **User**) | ❌ |
| List / revoke own links | ✅ (may revoke any) | ✅ own only | ❌ |
| Preview a link | anonymous | anonymous | anonymous |
| List / view users | everyone | users **they referred** | ❌ |
| Suspend / reactivate | ✅ | ❌ | ❌ |
| Referral history | all (filter by admin) | own | ❌ |
| Referral tree | ✅ | ❌ | ❌ |

Anything outside the caller's scope returns **404**, never 403, so ids cannot be probed.

---

## Invitations

### POST /invitations (Admin)
```json
{ "label": "Sector 21 WhatsApp group", "maxUses": 50, "expiresAt": "2026-12-31T18:29:59Z" }
```
All fields are optional: an empty body `{}` creates an unlimited, non-expiring link.

**201 Created** (`Location: /api/v1/invitations/K7QX2M9PRA`)
```json
{
  "id": "0193a2b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b",
  "code": "K7QX2M9PRA",
  "link": "https://sos.example.com/i/K7QX2M9PRA",
  "inviteeRole": "User",
  "label": "Sector 21 WhatsApp group",
  "maxUses": 50,
  "usedCount": 0,
  "expiresAt": "2026-12-31T18:29:59Z",
  "status": "Active",
  "createdAt": "2026-11-14T10:00:00Z"
}
```
| Status | `code` |
|---|---|
| 400 | `validation_failed` (`maxUses` 1–10,000; `expiresAt` in the future and ≤ 1 year ahead) |
| 403 | `forbidden` (caller is a normal User) |
| 422 | `invitation.limit_reached` (100 active links per admin) |

### GET /invitations?status=Active&page=1&pageSize=20 (Admin)
Own links, newest first. `status` is one of `Active` | `Revoked` | `Expired` | `Exhausted`; omit it for all.
```json
{
  "items": [ { "id": "…", "code": "K7QX2M9PRA", "link": "https://sos.example.com/i/K7QX2M9PRA", "inviteeRole": "User", "usedCount": 12, "maxUses": 50, "status": "Active", "createdAt": "…" } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

### DELETE /invitations/{id} (Admin)
**204 No Content**. People who already joined keep their permanent mapping. **404** if the link isn't yours (the Master Admin may revoke any link).

### GET /invitations/{code} (anonymous, 20 req/min/IP)
Shown on the sign-up screen before OTP. Codes are case-insensitive.
```json
{
  "code": "K7QX2M9PRA",
  "status": "Active",
  "inviteeRole": "User",
  "invitedBy": { "fullName": "Mehta Ward Lead", "role": "SubAdmin" },
  "expiresAt": "2026-12-31T18:29:59Z"
}
```
- When `status` isn't `Active` (revoked, expired, used up, or the inviter is suspended), `invitedBy` is omitted. The app shows "This link is no longer valid. Ask your community admin for a new one."
- **404** `invitation.not_found` for unknown or malformed codes.
- The inviter's phone number is never exposed.

### Link lifecycle
`Active` → `Revoked` (admin action or suspended inviter) / `Expired` (`expiresAt` passed) / `Exhausted` (`usedCount = maxUses`). Revocation takes precedence over expiry, and expiry over exhaustion. Usage is counted atomically (row-version check), so `maxUses` can't be exceeded under concurrent sign-ups.

---

## Admin: users

### GET /admin/users (Admin)
Query: `role`, `status` (default excludes `Deleted`), `search`, `referredBy` (Master only), `page`, `pageSize` (≤ 100).

`search` is either a **name fragment** or a **phone number**. Phone matches are exact, and a 10-digit Indian mobile such as `9876543210` is treated as `+919876543210`.
```json
{
  "items": [
    {
      "id": "0193a1c2-d4e5-7f60-8a9b-0c1d2e3f4a5b",
      "fullName": "Asha Patel",
      "phoneNumber": "+919876543210",
      "role": "User",
      "status": "Active",
      "referredBy": { "id": "0193a0ff-…", "fullName": "Mehta Ward Lead", "role": "SubAdmin", "status": "Active" },
      "referredUsersCount": 0,
      "alertsEnabled": true,
      "createdAt": "2026-11-14T10:00:00Z",
      "lastLoginAt": "2026-11-14T10:02:00Z"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```
The phone number is shown to the referring admin and the Master Admin so they can call during an emergency. It is `null` for deleted accounts.

### GET /admin/users/{id} (Admin)
```json
{
  "user": { "...": "AdminUserListItem as above" },
  "email": "asha@example.com",
  "acceptedPolicyVersion": "2026-11",
  "referral": { "invitationId": "…", "invitationLabel": "Sector 21 WhatsApp group", "registeredAt": "2026-11-14T10:00:00Z" },
  "sosRaised": 1,
  "activeInvitations": 0
}
```
Health data (blood group, emergency contact) and location are **not** shown here. The assigned admin sees them only when reviewing an SOS (Module 3e).

### PUT /admin/users/{id}/status (Master Admin)
```json
{ "status": "Suspended", "reason": "Repeated false SOS alerts" }
```
```json
{ "status": "Active" }
```
**200 OK**: `AdminUserDetail`.

Effects of suspension:
- every session is revoked and the account is rejected within about 30 s, even with a valid access token;
- a suspended Sub-Admin's invitation links stop working, and their Users' new SOS requests go to the Master Admin (Module 3d);
- a `UserSuspended` / `UserReactivated` audit event is written with the reason.

| Status | `code` |
|---|---|
| 400 | `validation_failed` (a `reason` is required to suspend; `status` must be `Active` or `Suspended`) |
| 403 | `forbidden` (Sub-Admins cannot change status) |
| 422 | `user.cannot_change_own_status`, `user.cannot_suspend_master`, `user.deleted` |

---

## Admin: referrals

### GET /admin/referrals?adminId=&from=&to=&page= (Admin)
The permanent history of who joined through whom. Sub-Admins always get their own; the Master Admin can filter by `adminId`. `from`/`to` filter on registration time (half-open `[from, to)`).
```json
{
  "items": [
    {
      "id": 1042,
      "referredUser": { "id": "…", "fullName": "Asha Patel", "role": "User", "status": "Active" },
      "referrer": { "id": "…", "fullName": "Mehta Ward Lead", "role": "SubAdmin", "status": "Active" },
      "invitationId": "…",
      "invitationLabel": "Sector 21 WhatsApp group",
      "assignedRole": "User",
      "registeredAt": "2026-11-14T10:00:00Z"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

### GET /admin/referrals/tree (Master Admin)
```json
{
  "root": { "id": "…", "fullName": "Community Master Admin", "role": "MasterAdmin", "status": "Active" },
  "totalSubAdmins": 2,
  "totalUsers": 57,
  "subAdmins": [
    {
      "subAdmin": { "id": "…", "fullName": "Mehta Ward Lead", "role": "SubAdmin", "status": "Active" },
      "phoneNumber": "+919812345678",
      "referredUsers": 41,
      "activeUsers": 40,
      "activeInvitations": 2,
      "joinedAt": "2026-11-01T09:00:00Z"
    }
  ]
}
```
To drill down into one Sub-Admin, call `GET /admin/users?referredBy={subAdminId}`.

---

## Invitation links on the phone

Done in Module 4a ([04a](04a-mobile-foundation.md#invite-links)): the app route `/i/{code}`, App Links and Universal Links configuration, and the `web/invite-site/` landing page with its `.well-known` files.

Still needed:
- Set `App:InviteLinkBaseUrl` to the real domain (currently the placeholder `https://sos.example.com`).
- Deploy `web/invite-site/` on that domain, with the real signing fingerprints and Team ID filled in.
