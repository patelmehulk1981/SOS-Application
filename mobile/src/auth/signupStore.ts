import { create } from 'zustand';

interface SignupState {
  /** From an invite deep link (/i/{code}); used when the number turns out to be new. */
  pendingInviteCode: string | null;
  /** E.164 number being verified. */
  phone: string | null;
  setPendingInviteCode: (code: string | null) => void;
  setPhone: (phone: string | null) => void;
  reset: () => void;
}

/** In-memory state of the sign-in / sign-up flow. Nothing here is secret or needs to survive a restart. */
export const useSignupStore = create<SignupState>((set) => ({
  pendingInviteCode: null,
  phone: null,
  setPendingInviteCode: (pendingInviteCode) => set({ pendingInviteCode }),
  setPhone: (phone) => set({ phone }),
  reset: () => set({ pendingInviteCode: null, phone: null }),
}));
