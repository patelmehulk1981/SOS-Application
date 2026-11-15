import 'i18next';

import type { Strings } from './en';

declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation';
    resources: { translation: Strings };
  }
}
