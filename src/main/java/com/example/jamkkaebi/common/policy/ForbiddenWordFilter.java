package com.example.jamkkaebi.common.policy;

import org.springframework.stereotype.Component;

import java.text.Normalizer;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * 금칙어 필터. 닉네임과 정령 이름이 <b>함께</b> 쓴다 — 둘 다 친구 목록·요청함·전시관에 노출되므로
 * 기준이 갈리면 한쪽으로 빠져나간다.
 *
 * <p>여기 담긴 목록은 뼈대일 뿐이다. 실제 서비스 전에 운영 정책에 맞는 목록으로 채워야 하며,
 * 목록이 길어지면 파일이나 설정으로 빼는 것이 낫다.
 */
@Component
public class ForbiddenWordFilter {

    private static final Set<String> FORBIDDEN_WORDS = Set.of(
            "관리자", "운영자", "운영팀", "고객센터", "잠깨비운영",
            "admin", "administrator", "gm", "system", "operator"
    );

    /**
     * 금칙어가 섞여 있는지 본다.
     *
     * <p>비교 전에 <b>공백·특수문자를 지우고</b> 소문자로 맞춘다 — 그러지 않으면 {@code 관 리 자},
     * {@code a-d-m-i-n} 처럼 사이를 벌린 값이 그대로 통과한다. 부분 일치까지 막는 이유는
     * {@code 관리자입니다} 같은 형태를 잡기 위해서다.
     */
    public boolean containsForbiddenWord(String text) {
        if (text == null || text.isBlank()) {
            return false;
        }
        String normalized = normalize(text);
        return FORBIDDEN_WORDS.stream().anyMatch(normalized::contains);
    }

    private String normalize(String text) {
        String decomposed = Normalizer.normalize(text, Normalizer.Form.NFKC);
        return decomposed.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9가-힣]", "");
    }

    /** 설정·테스트에서 현재 목록을 확인할 수 있게 노출한다. */
    public List<String> words() {
        return FORBIDDEN_WORDS.stream().sorted().toList();
    }
}
