<script setup lang="ts">
import { computed } from 'vue'
import DefaultTheme from 'vitepress/theme'
import { useData } from 'vitepress'
import AnimatedLogo from './AnimatedLogo.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()

const isHome = computed(() => frontmatter.value?.layout === 'home')
</script>

<template>
  <Layout>
    <template #home-hero-image>
      <AnimatedLogo v-if="isHome" />
    </template>
  </Layout>
</template>
