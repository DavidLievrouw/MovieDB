window.filmsSetCookie = (name, value) => {
    document.cookie = name + '=' + value + ';path=/;max-age=31536000;samesite=lax';
    if (name === 'films-theme') {
        document.documentElement.dataset.theme = value;
    }
};
