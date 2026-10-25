// Voice for the studio: speak a question, hear the verdict.
// Uses the browser's own speech services (Edge and Chrome), so there are no keys and no extra Azure resources.

const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
let recognizer = null;
let speakToken = 0;

export function supported() {
    return {
        listen: !!Recognition && window.isSecureContext,
        speak: "speechSynthesis" in window
    };
}

// Streams what the browser hears into .NET: OnVoiceHeard(text, isFinal), then OnVoiceEnded(error or null).
export function listen(dotnet, lang) {
    stopListening();
    stopSpeaking();

    const r = new Recognition();
    r.lang = lang;
    r.interimResults = true;
    r.continuous = false;
    r.maxAlternatives = 1;

    let finalText = "";
    let error = null;

    r.onresult = e => {
        let interim = "";
        for (let i = e.resultIndex; i < e.results.length; i++) {
            const text = e.results[i][0].transcript;
            if (e.results[i].isFinal) finalText += text; else interim += text;
        }
        dotnet.invokeMethodAsync("OnVoiceHeard", (finalText + interim).trim(), false);
    };
    r.onerror = e => { error = e.error; };
    r.onend = () => {
        recognizer = null;
        if (!error && finalText.trim()) dotnet.invokeMethodAsync("OnVoiceHeard", finalText.trim(), true);
        dotnet.invokeMethodAsync("OnVoiceEnded", error ?? (finalText.trim() ? null : "no-speech"));
    };

    recognizer = r;
    r.start();
}

export function stopListening() {
    if (recognizer) { recognizer.stop(); }
}

function pickVoice(lang) {
    const voices = speechSynthesis.getVoices();
    const base = lang.split("-")[0].toLowerCase();
    const matching = voices.filter(v => v.lang.toLowerCase().replace("_", "-").startsWith(base));
    // Prefer the exact region, then the most natural-sounding ("Natural"/"Online" voices in Edge, "Google" in Chrome).
    const score = v => (v.lang.toLowerCase() === lang.toLowerCase() ? 2 : 0) + (/natural|online|google/i.test(v.name) ? 1 : 0);
    return matching.sort((a, b) => score(b) - score(a))[0] ?? null;
}

// Reads text aloud sentence by sentence (Chrome cuts long utterances short). Returns false if no voice speaks that language.
export async function speak(dotnet, text, lang) {
    stopSpeaking();
    const token = ++speakToken;

    if (speechSynthesis.getVoices().length === 0) {
        await new Promise(resolve => {
            speechSynthesis.addEventListener("voiceschanged", resolve, { once: true });
            setTimeout(resolve, 1500);
        });
    }
    const voice = pickVoice(lang);
    if (!voice) return false;

    const sentences = text.match(/[^.!?。！？]+[.!?。！？]*\s*/g) ?? [text];
    sentences.forEach((s, i) => {
        const u = new SpeechSynthesisUtterance(s.trim());
        u.voice = voice;
        u.lang = voice.lang;
        u.rate = 1;
        if (i === sentences.length - 1) {
            u.onend = u.onerror = () => { if (token === speakToken) dotnet.invokeMethodAsync("OnSpeechEnded"); };
        }
        speechSynthesis.speak(u);
    });
    return true;
}

export function stopSpeaking() {
    speakToken++;
    if ("speechSynthesis" in window) speechSynthesis.cancel();
}
