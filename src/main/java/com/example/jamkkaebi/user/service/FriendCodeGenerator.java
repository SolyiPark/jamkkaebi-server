package com.example.jamkkaebi.user.service;

import com.example.jamkkaebi.user.domain.User;
import org.springframework.stereotype.Component;

import java.security.SecureRandom;

/**
 * 친구 코드를 발급한다. 8자리, 무작위, 재발급 없음.
 *
 * <p><b>순번이 아니라 무작위여야 한다.</b> 친구 코드는 밖으로 나가는 유일한 식별자인데, 순번이면
 * 값 하나만 알아도 위아래를 훑어 전체 계정을 열거할 수 있다.
 *
 * <p>사람이 눈으로 읽고 손으로 입력하는 값이라 <b>헷갈리는 글자를 뺀다</b> — {@code 0/O},
 * {@code 1/I/L} 이 섞이면 친구 코드를 불러 주다 틀리는 일이 계속 생긴다. 남은 31자 × 8자리면
 * 약 8500억 가지라, 사용자가 수십만 명이 돼도 충돌은 드물다.
 */
@Component
public class FriendCodeGenerator {

    /** 0·O·1·I·L 을 뺀 31자. 입력 검사와 같은 집합이어야 해서 {@link FriendCodeFormat} 에 둔다. */
    private static final char[] ALPHABET = FriendCodeFormat.ALPHABET.toCharArray();

    private final SecureRandom random = new SecureRandom();

    public String generate() {
        StringBuilder code = new StringBuilder(User.FRIEND_CODE_LENGTH);
        for (int i = 0; i < User.FRIEND_CODE_LENGTH; i++) {
            code.append(ALPHABET[random.nextInt(ALPHABET.length)]);
        }
        return code.toString();
    }
}
