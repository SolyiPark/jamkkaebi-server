package com.example.jamkkaebi.global.time;

import org.springframework.stereotype.Component;

import java.time.Clock;
import java.time.LocalDate;
import java.time.LocalDateTime;

/**
 * 게임 일자를 읽는다.
 *
 * <p><b>하루는 KST 자정에 바뀐다.</b> 상자 선물·방문 보상·오늘의 추천·친구 코드 조회 한도처럼 "하루에
 * 몇 번"인 규칙은 전부 {@link #today()} 로 날짜를 정한다. 기준이 기능마다 갈리면 선물은 리셋됐는데
 * 추천은 어제 것이 남는 식으로 어긋난다.
 */
@Component
public class GameClock {

    private final Clock clock;

    public GameClock(Clock clock) {
        this.clock = clock;
    }

    public LocalDateTime now() {
        return LocalDateTime.now(clock);
    }

    public LocalDate today() {
        return LocalDate.now(clock);
    }

    /** 다음 게임 일자가 시작되는 시각(다음 자정). 클라이언트가 리셋 시각을 상수로 두지 않게 내려준다. */
    public LocalDateTime nextResetAt() {
        return today().plusDays(1).atStartOfDay();
    }
}
