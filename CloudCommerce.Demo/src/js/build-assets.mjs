import { readFile, writeFile } from "node:fs/promises";
import less from "less";
import CleanCSS from "less-plugin-clean-css";

const root = new URL("../../", import.meta.url);
const source = await readFile(new URL("src/css/demo.less", root), "utf8");
const normal = await less.render(source);
const minified = await less.render(source, { plugins: [new CleanCSS({ advanced: true })] });
await writeFile(new URL("wwwroot/css/demo.css", root), normal.css);
await writeFile(new URL("wwwroot/css/demo.min.css", root), minified.css);
await writeFile(new URL("wwwroot/js/demo.js", root), await readFile(new URL("src/js/demo.js", root)));
const componentRoot = new URL("../CloudCommerce.Components/", root);
const componentSource = await readFile(new URL("src/css/cloud-commerce.less", componentRoot), "utf8");
await writeFile(new URL("wwwroot/css/cloud-commerce.css", componentRoot), (await less.render(componentSource)).css);
