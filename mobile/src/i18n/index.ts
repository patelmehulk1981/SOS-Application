import { getLocales } from 'expo-localization';
import { createInstance } from 'i18next';
import { initReactI18next } from 'react-i18next';

import { en } from './en';

export const resources = { en: { translation: en } } as const;
export const supportedLanguages = Object.keys(resources) as (keyof typeof resources)[];

function deviceLanguage(): string {
  const code = getLocales()[0]?.languageCode ?? 'en';
  return supportedLanguages.includes(code as keyof typeof resources) ? code : 'en';
}

const i18n = createInstance();

// initReactI18next registers this instance for useTranslation(); init is synchronous with inline resources.
void i18n.use(initReactI18next).init({
  resources,
  lng: deviceLanguage(),
  fallbackLng: 'en',
  interpolation: { escapeValue: false }, // React already escapes
  returnNull: false,
  initAsync: false, // resources are bundled: translate synchronously from the first render
});

export default i18n;
