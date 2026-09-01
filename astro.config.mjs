import { defineConfig } from 'astro/config';

export default defineConfig({
  site: 'https://roslyn-tools.danmarshall.dev',
  srcDir: './site',
  publicDir: './site/public',
  outDir: './artifacts/site',
  trailingSlash: 'always',
  build: {
    assets: '_astro',
  },
  markdown: {
    shikiConfig: {
      theme: 'github-dark',
    },
  },
});
