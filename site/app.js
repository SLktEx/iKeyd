(() => {
  const header = document.querySelector('[data-header]');
  const menuButton = document.querySelector('[data-menu-button]');
  const nav = document.querySelector('[data-nav]');
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const updateHeader = () => {
    if (header) header.classList.toggle('is-scrolled', window.scrollY > 4);
  };

  updateHeader();
  window.addEventListener('scroll', updateHeader, { passive: true });

  if (menuButton && nav) {
    menuButton.addEventListener('click', () => {
      const open = menuButton.getAttribute('aria-expanded') === 'true';
      menuButton.setAttribute('aria-expanded', String(!open));
      nav.classList.toggle('is-open', !open);
    });

    nav.addEventListener('click', (event) => {
      if (event.target instanceof HTMLAnchorElement) {
        menuButton.setAttribute('aria-expanded', 'false');
        nav.classList.remove('is-open');
      }
    });
  }

  const revealItems = [...document.querySelectorAll('.reveal')];
  if (reducedMotion || !('IntersectionObserver' in window)) {
    revealItems.forEach((item) => item.classList.add('is-visible'));
  } else {
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.12 });
    revealItems.forEach((item) => observer.observe(item));
  }

  const demos = {
    'tap-a': {
      keys: ['A'],
      hold: [],
      state: 'BASE',
      input: 'A · tap',
      output: 'Z',
      code: 'A = LT(NUM, Z)'
    },
    'hold-a': {
      keys: ['A'],
      hold: ['A'],
      state: 'NUM',
      input: 'A · hold',
      output: 'NUM layer active',
      code: 'A = LT(NUM, Z)'
    },
    'combo-kq': {
      keys: ['K', 'Q'],
      hold: [],
      state: 'BASE',
      input: 'K + Q',
      output: '"fa"',
      code: 'combo K + Q = "fa"'
    }
  };

  const demoButtons = [...document.querySelectorAll('[data-demo]')];
  const keyElements = [...document.querySelectorAll('[data-key]')];
  const stateLabel = document.querySelector('[data-state-label]');
  const inputLabel = document.querySelector('[data-demo-input]');
  const outputLabel = document.querySelector('[data-demo-output]');
  const codeLabel = document.querySelector('[data-demo-code]');

  const selectDemo = (name) => {
    const demo = demos[name];
    if (!demo) return;

    demoButtons.forEach((button) => {
      const selected = button.dataset.demo === name;
      button.classList.toggle('is-selected', selected);
      button.setAttribute('aria-pressed', String(selected));
    });

    keyElements.forEach((key) => {
      const keyName = key.dataset.key;
      key.classList.toggle('is-active', demo.keys.includes(keyName));
      key.classList.toggle('is-hold', demo.hold.includes(keyName));
    });

    if (stateLabel) stateLabel.textContent = demo.state;
    if (inputLabel) inputLabel.textContent = demo.input;
    if (outputLabel) outputLabel.textContent = demo.output;
    if (codeLabel) codeLabel.textContent = demo.code;
  };

  demoButtons.forEach((button) => {
    button.setAttribute('aria-pressed', String(button.classList.contains('is-selected')));
    button.addEventListener('click', () => selectDemo(button.dataset.demo));
  });

  selectDemo('tap-a');
})();
