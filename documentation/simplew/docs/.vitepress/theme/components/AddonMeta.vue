<script setup>
import { computed } from 'vue'

const props = defineProps({
    package: {
        type: String,
        required: true,
    },
    status: {
        type: String,
        default: '',
        validator: value => value === 'official' || value === 'community' || value === '',
    },
    experimental: {
        type: Boolean,
        default: false,
    },
    license: {
        type: String,
        default: '',
    },
    licenseUrl: {
        type: String,
        default: '',
    },
})

const packageUrl = computed(() => `https://www.nuget.org/packages/${encodeURIComponent(props.package)}`)
const shieldPackage = computed(() => encodeURIComponent(props.package))
const statusLabel = computed(() => props.status === 'official' ? 'official' : 'community')
const statusColor = computed(() => props.status === 'official' ? '2e7d32' : '2563eb')
const statusTitle = computed(() => props.status === 'official' ? 'maintained by the SimpleW project' : 'maintained independently by its author')
const statusBadgeUrl = computed(() => `https://img.shields.io/badge/addon-${statusLabel.value}-${statusColor.value}?style=flat`)
const experimentalBadgeUrl = 'https://img.shields.io/badge/maturity-experimental-b45309?style=flat'
const experimentalTitle = 'This package is currently experimental. Its API and behavior may change without notice.'
const versionBadgeUrl = computed(() => `https://img.shields.io/nuget/v/${shieldPackage.value}?color=7737d1&label=version&logo=NuGet&style=flat`)
const downloadsBadgeUrl = computed(() => `https://img.shields.io/nuget/dt/${shieldPackage.value}?color=7737d1&label=downloads&logo=NuGet&style=flat`)
const licenseBadgeUrl = computed(() => props.license
                                        ? `https://img.shields.io/badge/license-${encodeURIComponent(props.license)}-7737d1?style=flat`
                                        : 'https://img.shields.io/badge/license-not%20declared-lightgrey?style=flat')
const licenseAlt = computed(() => props.license
                                    ? `${props.license} license`
                                    : 'License not declared')
</script>

<template>
    <div class="addon-meta" role="group" :aria-label="`Package metadata for ${props.package}`">
        <span class="addon-meta__item" :title="statusTitle" v-if="props.status">
            <img :src="statusBadgeUrl" :alt="`${statusLabel} addon`" height="20" />
        </span>
        <span v-if="props.experimental" class="addon-meta__item" :title="experimentalTitle">
            <img :src="experimentalBadgeUrl" alt="Experimental package" height="20" />
        </span>
        <a class="addon-meta__item no-link-decoration no-external-link-icon" :href="packageUrl" target="_blank" rel="noopener noreferrer" >
            <img :src="versionBadgeUrl" :alt="`Latest stable version of ${props.package} on NuGet`" height="20" />
        </a>
        <a class="addon-meta__item no-link-decoration no-external-link-icon" :href="packageUrl" target="_blank" rel="noopener noreferrer">
            <img :src="downloadsBadgeUrl" :alt="`Total downloads of ${props.package} on NuGet`" height="20" />
        </a>
        <a v-if="props.license && props.licenseUrl" class="addon-meta__item no-link-decoration no-external-link-icon" :href="props.licenseUrl" target="_blank" rel="noopener noreferrer">
            <img :src="licenseBadgeUrl" :alt="licenseAlt" height="20" />
        </a>
        <span v-else class="addon-meta__item">
            <img :src="licenseBadgeUrl" :alt="licenseAlt" height="20" />
        </span>
    </div>
</template>
