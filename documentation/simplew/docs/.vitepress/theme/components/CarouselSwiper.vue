<script setup lang="ts">
import { computed } from 'vue'
import { withBase } from 'vitepress'

// Swiper Vue (client-side only)
import { Swiper, SwiperSlide } from 'swiper/vue'
import { Navigation, Pagination, A11y } from 'swiper/modules'

// styles swiper
import 'swiper/css'
import 'swiper/css/navigation'
import 'swiper/css/pagination'

type Item = { src: string; alt?: string; caption?: string; href?: string }

const props = defineProps<{
  items: Item[]
  title?: string
  loop?: boolean
}>()

const modules = [Navigation, Pagination, A11y]

const normalized = computed(() =>
  props.items.map(it => ({
    ...it,
    src: withBase(it.src),
  }))
)

// to use :
// < script setup >
// const items = [
//   { src: '/public/snippets/observability-with-uptrace.png', alt: 'screen 1', caption: 'Accueil' },
//   { src: '/public/snippets/observability-with-uptrace.png', alt: 'screen 2', caption: 'Config' },
//   { src: '/public/snippets/observability-with-uptrace.png', alt: 'screen 3', caption: 'Logs' }
// ]
// </ script >
// < CarouselSwiper title="Screenshots" :items="items" loop />

</script>

<template>
  <section class="vp-swiper">
    <h3 v-if="title" class="vp-swiper__title">{{ title }}</h3>

    <ClientOnly>
      <Swiper
        :modules="modules"
        :loop="!!loop"
        :navigation="true"
        :pagination="{ clickable: true }"
        :spaceBetween="12"
        :slidesPerView="1"
      >
        <SwiperSlide v-for="(it, i) in normalized" :key="i">
          <component :is="it.href ? 'a' : 'div'" :href="it.href" class="vp-swiper__card">
            <img class="vp-swiper__img" :src="it.src" :alt="it.alt || ''" loading="lazy" />
            <p v-if="it.caption" class="vp-swiper__caption">{{ it.caption }}</p>
          </component>
        </SwiperSlide>
      </Swiper>
    </ClientOnly>
  </section>
</template>

<style scoped>
.vp-swiper {
  margin: 16px 0;
}

.vp-swiper__title {
  margin: 0 0 8px 0;
  font-size: 16px;
}

.vp-swiper__card {
  display: block;
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  background: var(--vp-c-bg-soft);
  overflow: hidden;
  text-decoration: none;
  color: inherit;
}

.vp-swiper__img {
  width: 100%;
  height: 260px;
  object-fit: cover;
  display: block;
}

.vp-swiper__caption {
  margin: 0;
  padding: 10px 12px;
  font-size: 13px;
  color: var(--vp-c-text-2);
}
</style>