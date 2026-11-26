/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: 'class',
  // MUI owns the base reset; Tailwind is used for layout utilities only.
  corePlugins: { preflight: false },
  important: '#root',
  theme: { extend: {} },
  plugins: [],
}
