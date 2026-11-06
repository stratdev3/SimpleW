<script setup>
import { onBeforeUnmount, ref } from 'vue'

defineProps({
  src: { type: String, required: true },
  alt: { type: String, required: true }
})

const dialog = ref(null)
let previousOverflow

function open() {
  if (!dialog.value || dialog.value.open) return
  dialog.value.showModal()
  previousOverflow = document.body.style.overflow
  document.body.style.overflow = 'hidden'
}

function restoreScroll() {
  if (previousOverflow === undefined) return
  document.body.style.overflow = previousOverflow
  previousOverflow = undefined
}

onBeforeUnmount(restoreScroll)
</script>

<template>
  <button class="image-preview" type="button" :aria-label="`Enlarge image: ${alt}`" @click="open">
    <img :src="src" :alt="alt" loading="lazy" />
  </button>

  <dialog ref="dialog" class="image-dialog" :aria-label="alt" @click="dialog.close()" @close="restoreScroll">
    <img :src="src" :alt="alt" />
  </dialog>
</template>

<style scoped>
.image-preview {
  display: block;
  width: 100%;
  margin: 16px 0;
  padding: 0;
  border: 0;
  border-radius: 8px;
  background: transparent;
  cursor: zoom-in;
}

.image-preview img {
  width: 100%;
  border-radius: 8px;
}

.image-hint {
  display: block;
  margin-top: 6px;
  color: var(--vp-c-text-2);
  font-size: 13px;
  text-align: right;
}

.image-preview:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 4px;
}

.image-dialog {
  position: fixed;
  inset: 0;
  width: calc(100vw - 32px);
  max-width: 1800px;
  max-height: calc(100dvh - 32px);
  margin: auto;
  padding: 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  cursor: zoom-out;
  box-shadow: 0 16px 64px rgb(0 0 0 / 35%);
}

.image-dialog::backdrop {
  background: rgb(0 0 0 / 80%);
}

.image-dialog img {
  display: block;
  width: 100%;
  max-height: calc(100dvh - 60px);
  object-fit: contain;
}
</style>
