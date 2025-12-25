<script setup lang="ts">
import DefaultTheme from 'vitepress/theme'
import { computed } from 'vue'
import { useData } from 'vitepress'
import CarouselSwiperHero from './CarouselSwiperHero.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()

const items = computed(() => (frontmatter.value?.carousel ?? []) as any[])
</script>

<template>
  <Layout>
    <!-- replace hero image -->
    <template #home-hero-image>
      <div class="home-carousel" v-if="items.length">
        <CarouselSwiperHero :items="items" loop />
      </div>
    </template>
  </Layout>
</template>

<style scoped>
.home-carousel {
  width: min(520px, 92vw);
}
</style>
