<script setup lang="ts">
import { computed } from 'vue'
import { withBase } from 'vitepress'

// Swiper Vue (client-side only)
import { Swiper, SwiperSlide } from 'swiper/vue'
import { Navigation, Pagination, A11y, Autoplay, EffectFade } from 'swiper/modules'

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

const modules = [Navigation, Pagination, A11y, Autoplay, EffectFade]

const normalized = computed(() =>
  props.items.map(it => ({
    ...it,
    src: withBase(it.src),
  }))
)
</script>

<template>
  <section class="vp-swiper">
    <ClientOnly>
      <!-- :pagination="{ clickable: true }" :autoplay="{ delay: 4500, disableOnInteraction: false, pauseOnMouseEnter: true }" -->
      <Swiper
        :modules="modules"
        :loop="!!loop"
        :navigation="true"
        :pagination="false"
        :spaceBetween="12"
        :slidesPerView="1"
        :autoplay="{ delay: 4500, disableOnInteraction: false, pauseOnMouseEnter: true }"
      >
        <SwiperSlide v-for="(it, i) in normalized" :key="i">
          <component :is="it.href ? 'a' : 'div'" :href="it.href" class="vp-swiper__card">
            <img class="vp-swiper__img" :src="it.src" :alt="it.alt || ''" loading="lazy" />
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
.vp-swiper__card {
  display: block;
  overflow: hidden;
  text-decoration: none;
  color: inherit;
}
.vp-swiper__img {
  width: 100%;
  max-height: 320px;
  object-fit: contain;
  display: block;
}
.vp-swiper__pagination {
  margin-top: 8px;
  display: flex;
  justify-content: center;
}
</style>
