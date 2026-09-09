const $ = (sel, ctx=document) => ctx.querySelector(sel);
const $$ = (sel, ctx=document) => [...ctx.querySelectorAll(sel)];

const pageStyles = document.createElement('link');
pageStyles.rel = 'stylesheet';
pageStyles.href = 'pages.css';
document.head.appendChild(pageStyles);

const premiumStyles = document.createElement('link');
premiumStyles.rel = 'stylesheet';
premiumStyles.href = 'premium.css?v=4';
document.head.appendChild(premiumStyles);

const header = $('.site-header');
const menuBtn = $('.menu-btn');
const navLinks = $('.nav-links');
const themeBtn = $('#themeToggle');
const root = document.documentElement;

function setHeader(){ header?.classList.toggle('scrolled', window.scrollY > 18); }
setHeader();
window.addEventListener('scroll', setHeader, {passive:true});

menuBtn?.addEventListener('click', () => {
  const open = navLinks.classList.toggle('open');
  menuBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
});
$$('.nav-links a').forEach(a => a.addEventListener('click', () => navLinks?.classList.remove('open')));

const storedTheme = localStorage.getItem('orrbit-theme');
if (storedTheme) root.setAttribute('data-theme', storedTheme);
function updateThemeIcon(){ if(themeBtn) themeBtn.textContent = root.getAttribute('data-theme') === 'light' ? '☾' : '☼'; }
updateThemeIcon();
themeBtn?.addEventListener('click', () => {
  const next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
  root.setAttribute('data-theme', next);
  localStorage.setItem('orrbit-theme', next);
  updateThemeIcon();
});

const observer = new IntersectionObserver(entries => {
  entries.forEach(e => { if(e.isIntersecting){ e.target.classList.add('visible'); observer.unobserve(e.target); } });
}, {threshold:.12});
$$('.reveal').forEach(el => observer.observe(el));

if (window.matchMedia('(pointer:fine)').matches && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
  const glow = document.createElement('div'); glow.className = 'cursor-glow'; document.body.appendChild(glow);
  window.addEventListener('pointermove', e => { glow.style.left = `${e.clientX}px`; glow.style.top = `${e.clientY}px`; }, {passive:true});
}

const modal = $('#projectModal');
$$('[data-open-project]').forEach(btn => btn.addEventListener('click', e => { e.preventDefault(); modal?.classList.add('open'); document.body.style.overflow='hidden'; }));
$$('[data-close-modal]').forEach(btn => btn.addEventListener('click', () => { modal?.classList.remove('open'); document.body.style.overflow=''; }));
modal?.addEventListener('click', e => { if(e.target === modal){ modal.classList.remove('open'); document.body.style.overflow=''; } });
window.addEventListener('keydown', e => { if(e.key==='Escape' && modal?.classList.contains('open')) { modal.classList.remove('open'); document.body.style.overflow=''; } });

function whatsappFromForm(form){
  const data = new FormData(form);
  const fields = [...data.entries()].filter(([,v]) => String(v).trim());
  const lines = ['Hello oRRbit Team,', '', 'I would like to discuss a software project.'];
  fields.forEach(([k,v]) => lines.push(`${k}: ${v}`));
  lines.push('', 'Please guide me with the next steps.');
  const url = `https://wa.me/917489924776?text=${encodeURIComponent(lines.join('\n'))}`;
  window.open(url, '_blank', 'noopener,noreferrer');
}
$$('form[data-whatsapp-form]').forEach(form => form.addEventListener('submit', e => { e.preventDefault(); if(form.reportValidity()) whatsappFromForm(form); }));

const year = new Date().getFullYear();
$$('[data-year]').forEach(el => el.textContent = year);

(async function loadOfficialORRbitBrand(){
  try {
    const response = await fetch('brand-logo.b64?v=3', {cache:'force-cache'});
    if(!response.ok) throw new Error('Official logo asset failed');
    const encoded = (await response.text()).trim();
    const logoSrc = `data:image/webp;base64,${encoded}`;
    $$('.brand').forEach(brand => {
      brand.classList.add('brand--official');
      brand.innerHTML = `<img class="brand-logo" src="${logoSrc}" alt="oRRbit™" />`;
    });
  } catch (error) {
    console.warn('Official oRRbit logo could not be loaded; text brand retained.', error);
  }
})();
