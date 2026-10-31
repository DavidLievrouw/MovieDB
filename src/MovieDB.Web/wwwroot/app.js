window.filmsSetCookie = (name, value) => {
    document.cookie = name + '=' + value + ';path=/;max-age=31536000;samesite=lax';
    if (name === 'films-theme') {
        document.documentElement.dataset.theme = value;
    }
};

// Static pages have no circuit: the theme toggle sets the cookie and reloads instead.
document.addEventListener('click', (event) => {
    const toggle = event.target instanceof Element ? event.target.closest('[data-static-theme-toggle]') : null;
    if (toggle) {
        const next = document.documentElement.dataset.theme === 'light' ? 'dark' : 'light';
        window.filmsSetCookie('films-theme', next);
        window.location.reload();
        return;
    }

    // The user menu is a native <details>: close it on outside clicks and when a link inside it is used.
    document.querySelectorAll('details.films-user-menu[open]').forEach((menu) => {
        if (!menu.contains(event.target) || (event.target instanceof Element && event.target.closest('a'))) {
            menu.removeAttribute('open');
        }
    });
});

document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
        document.querySelectorAll('details.films-user-menu[open]').forEach((menu) => {
            menu.removeAttribute('open');
            menu.querySelector('summary')?.focus();
        });
    }
});
