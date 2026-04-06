<script setup lang="ts">
import DefaultTheme from 'vitepress/theme'
import { useData } from 'vitepress'
import AnimatedLogo from './AnimatedLogo.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()

const isHome = frontmatter.value?.layout === 'home'
</script>

<template>
  <Layout>
    <template #home-hero-image v-if="isHome">
      <AnimatedLogo />
    </template>
  </Layout>
</template>
