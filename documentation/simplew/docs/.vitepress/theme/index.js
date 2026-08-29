// https://vitepress.dev/guide/custom-theme
import DefaultTheme from 'vitepress/theme'
import './style.css'
import './custom.css'
import Layout from './components/Layout.vue'
import AddonMeta from './components/AddonMeta.vue'

/** @type {import('vitepress').Theme} */
export default {
  extends: DefaultTheme,
  Layout,
  enhanceApp({ app, router, siteData }) {
    app.component('AddonMeta', AddonMeta)
  }
}
