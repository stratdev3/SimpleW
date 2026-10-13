<script setup lang="ts">
import { computed, ref } from 'vue'
import DefaultTheme from 'vitepress/theme'
import { useData } from 'vitepress'
import AnimatedLogo from './AnimatedLogo.vue'

const { Layout } = DefaultTheme
const { frontmatter } = useData()

const isHome = computed(() => frontmatter.value?.layout === 'home')
const heroApproach = ref(0)
</script>

<template>
  <Layout :style="{ '--hero-approach': heroApproach }">
    <template #home-hero-image>
      <AnimatedLogo v-if="isHome" @approach="heroApproach = $event" />
    </template>
  </Layout>
</template>

<style scoped>
:deep(.VPHero .image-bg) {
  opacity: calc(1 - var(--hero-approach, 0));
}

@media (max-width: 959px) {
  /* The close-up uses the whole image area, including its annotations. */
  :deep(.VPHero .image) {
    margin-top: -28px;
    margin-bottom: 20px;
  }
}
</style>
