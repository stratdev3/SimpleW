// https://vitepress.dev/guide/custom-theme
import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import './style.css'
import './custom.css'
import Layout from './components/Layout.vue'
// import CarouselSwiper from './components/CarouselSwiper.vue'
import CarouselSwiperHero from './components/CarouselSwiperHero.vue'

/** @type {import('vitepress').Theme} */
export default {
  extends: DefaultTheme,
  // Layout: () => {
  //   return h(DefaultTheme.Layout, null, {
  //     // https://vitepress.dev/guide/extending-default-theme#layout-slots
  //   })
  // },
  Layout,
  enhanceApp({ app, router, siteData }) {
    // app.component('CarouselSwiper', CarouselSwiper)
    app.component('CarouselSwiperHero', CarouselSwiperHero)
  }
}
