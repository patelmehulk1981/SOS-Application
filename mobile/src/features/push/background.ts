/** Side-effect module imported first by index.ts (imports are evaluated in order, before any app code). */
import { setBackgroundHandler } from './push';

setBackgroundHandler();
