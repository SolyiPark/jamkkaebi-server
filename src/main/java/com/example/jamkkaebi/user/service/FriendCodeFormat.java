package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.user.domain.User;

import java.util.Locale;
import java.util.Optional;

/**
 * 친구 코드의 문자 집합과 입력 정규화.
 *
 * <p>발급({@link FriendCodeGenerator})과 입력 검사가 <b>같은 문자 집합</b>을 봐야 한다. 둘이 갈리면
 * 발급된 코드가 조회에서 형식 오류로 막힌다.
 *
 * <p>화면에는 {@code K7QM-3PVX} 처럼 끊어 보이고, 사람이 옮겨 적다 보면 소문자나 공백이 섞이기 때문에
 * 하이픈·공백을 지우고 대문자로 맞춘 뒤 검사한다.
 */
public final class FriendCodeFormat {

    // 0·O·1·I·L 을 뺀 31자
    public static final String ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private FriendCodeFormat() {
    }

    /**
     * 입력값을 저장 형식(대문자 8자, 하이픈 없음)으로 바꾼다.
     *
     * @return 정규화한 코드. 길이나 문자가 맞지 않으면 비어 있다.
     */
    public static Optional<String> normalize(String raw) {
        if (raw == null) {
            return Optional.empty();
        }
        String code = raw.replaceAll("[\\s-]", "").toUpperCase(Locale.ROOT);
        if (code.length() != User.FRIEND_CODE_LENGTH) {
            return Optional.empty();
        }
        for (int i = 0; i < code.length(); i++) {
            if (ALPHABET.indexOf(code.charAt(i)) < 0) {
                return Optional.empty();
            }
        }
        return Optional.of(code);
    }
}
