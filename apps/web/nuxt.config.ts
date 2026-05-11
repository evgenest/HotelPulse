export default defineNuxtConfig({
  compatibilityDate: '2024-11-01',
  devtools: { enabled: true },

  // SPA mode — avoids SSR/CSR URL-split complexity for this demo
  ssr: false,

  runtimeConfig: {
    public: {
      apiBase: 'http://localhost:8080',
    },
  },

  css: ['~/assets/css/main.css'],

  app: {
    head: {
      title: 'HotelPulse',
      link: [
        { rel: 'preconnect', href: 'https://fonts.googleapis.com' },
        { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' },
        {
          rel: 'stylesheet',
          href: 'https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap',
        },
      ],
    },
  },
})
