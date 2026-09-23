package com.example.jamkkaebi.artifact.domain;

import java.util.List;

/**
 * 유물 정화의 세 페이즈. <b>순서가 곧 진행도</b>이다.
 *
 * <p>세 페이즈는 같은 게임이다 — 보드·도구·위협 공식이 전부 동일하고 이름과 서사만 다르다.
 */
public enum Phase {

    UNSEAL,  // 1페이즈 봉인 해제 — 봉인의 열쇠 조각
    RECALL,  // 2페이즈 기억 회복 — 기억의 조각
    GUARD;   // 3페이즈 정령 수호 — 오염 덩어리

    private static final List<Phase> ORDER = List.of(values());

    // 1~3. 클라이언트·로그에 숫자로 나갈 일이 있어 순번을 노출한다.
    public int order() {
        return ordinal() + 1;
    }

    public boolean isLast() {
        return this == GUARD;
    }

    // 다음 페이즈. 마지막({@link #GUARD})이면 {@code null}.
    public Phase next() {
        return isLast() ? null : ORDER.get(ordinal() + 1);
    }

    // {@code this} 보다 뒤에 오는가. 복구 순서에서 이미 클리어한 페이즈를 걷어낼 때 쓴다.
    public boolean isAfter(Phase other) {
        return other == null || ordinal() > other.ordinal();
    }

    public static List<Phase> from(Phase from) {
        return ORDER.subList(from.ordinal(), ORDER.size());
    }
}
