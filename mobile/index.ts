/**
 * App entry. Background work must be registered before the app component, because the OS may start
 * the JS runtime headless just to run it (Android delivering a push, iOS relaunching after a geofence
 * exit, a periodic check-in). Imports run in order, so both are set up before Expo Router registers the app.
 */
import '@/features/push/background';
import '@/features/location/background/tasks';
import 'expo-router/entry';
