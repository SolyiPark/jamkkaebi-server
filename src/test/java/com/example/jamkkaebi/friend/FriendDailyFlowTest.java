package com.example.jamkkaebi.friend;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.friend.domain.Block;
import com.example.jamkkaebi.friend.domain.FriendRecommendation;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.support.FakeExhibitionViewReader;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.hamcrest.Matchers.hasItem;
import static org.hamcrest.Matchers.startsWith;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 하루 단위 규칙(자정 리셋) — 상자 선물 · 방문 보상 · 오늘의 추천 — 과 접속 시점 전시관 스냅샷을 확인한다.
 */
class FriendDailyFlowTest extends FriendApiTestSupport {

    private static final LocalDateTime NEXT_DAY = LocalDateTime.of(2026, 9, 13, 0, 0, 1);

    @Test
    @DisplayName("선물은 하루에 친구 한 명에게만 — 자정이 지나면 다시 보낼 수 있고 받은 상자권은 사라진다")
    void giftOncePerDayResetsAtMidnight() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        Player carol = player("캐럴");
        makeFriends(alice, bob);
        makeFriends(alice, carol);

        post(alice, "/api/friends/{code}/gift", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("GIFT_SENT"))
                .andExpect(jsonPath("$.data.jeongseongGained").value(3))
                .andExpect(jsonPath("$.data.giftState").value("SENT"));
        assertThat(jeongseongOf(alice)).isEqualTo(3);
        assertThat(jeongseongOf(bob)).isZero();
        get(bob, "/api/friends").andExpect(jsonPath("$.data.today.giftTicketAvailable").value(true));

        post(alice, "/api/friends/{code}/gift", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.jeongseongGained").value(0));
        post(alice, "/api/friends/{code}/gift", carol.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F009"));
        get(alice, "/api/friends")
                .andExpect(jsonPath("$.data.today.giftSent").value(true))
                .andExpect(jsonPath("$.data.friends[?(@.friendCode == '%s')].giftState".formatted(bob.code()))
                        .value(hasItem("SENT")))
                .andExpect(jsonPath("$.data.friends[?(@.friendCode == '%s')].giftState".formatted(carol.code()))
                        .value(hasItem("DONE_TODAY")));

        clock.set(NEXT_DAY);

        get(bob, "/api/friends").andExpect(jsonPath("$.data.today.giftTicketAvailable").value(false));
        post(alice, "/api/friends/{code}/gift", carol.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.jeongseongGained").value(3));
        assertThat(jeongseongOf(alice)).isEqualTo(6);
    }

    @Test
    @DisplayName("받는 쪽은 첫 선물만 상자권, 다음 3건은 정성으로 환산, 그 뒤로는 받을 수 없다")
    void receiverConvertsExtraGiftsThenFills() throws Exception {
        Player bob = player("밥이");
        List<Player> senders = new ArrayList<>();
        for (int i = 0; i < 5; i++) {
            Player sender = player("보내는이" + i);
            makeFriends(sender, bob);
            senders.add(sender);
        }

        post(senders.get(0), "/api/friends/{code}/gift", bob.code()).andExpect(status().isOk());
        assertThat(jeongseongOf(bob)).isZero();
        for (int i = 1; i <= 3; i++) {
            post(senders.get(i), "/api/friends/{code}/gift", bob.code()).andExpect(status().isOk());
        }
        assertThat(jeongseongOf(bob)).isEqualTo(9);

        get(senders.get(4), "/api/friends").andExpect(jsonPath("$.data.friends[0].giftState").value("RECEIVER_FULL"));
        post(senders.get(4), "/api/friends/{code}/gift", bob.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F010"));
        get(bob, "/api/friends").andExpect(jsonPath("$.data.today.giftTicketAvailable").value(true));
    }

    @Test
    @DisplayName("방문 보상은 몇 명을 방문하든 하루 1회이고, 자정이 지나면 다시 받는다")
    void visitRewardOncePerDay() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        Player carol = player("캐럴");
        Player stranger = player("낯선이");
        makeFriends(alice, bob);
        makeFriends(alice, carol);

        post(alice, "/api/friends/{code}/visit-reward", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("VISIT_RECORDED"))
                .andExpect(jsonPath("$.data.jeongseongGained").value(5));
        post(alice, "/api/friends/{code}/visit-reward", carol.code())
                .andExpect(jsonPath("$.data.jeongseongGained").value(0));
        post(alice, "/api/friends/{code}/visit-reward", bob.code())
                .andExpect(jsonPath("$.data.jeongseongGained").value(0));
        assertThat(jeongseongOf(alice)).isEqualTo(5);
        get(alice, "/api/friends")
                .andExpect(jsonPath("$.data.today.visitRewardClaimed").value(true))
                .andExpect(jsonPath("$.data.friends[?(@.friendCode == '%s')].visitedToday".formatted(bob.code()))
                        .value(hasItem(true)));
        post(alice, "/api/friends/{code}/visit-reward", stranger.code())
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F008"));

        clock.set(NEXT_DAY);

        post(alice, "/api/friends/{code}/visit-reward", carol.code())
                .andExpect(jsonPath("$.data.jeongseongGained").value(5));
        assertThat(jeongseongOf(alice)).isEqualTo(10);
    }

    @Test
    @DisplayName("친구 전시관은 친구가 마지막으로 접속했을 때의 모습이다 — 그 뒤 바뀐 것은 다음 접속까지 보이지 않는다")
    void exhibitionShowsSnapshotFromLastAccess() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        makeFriends(owner, visitor);

        exhibitionViewReader.show(owner.id(), viewWithSpirit("향로지기"));
        get(owner, "/api/users/me").andExpect(status().isOk());
        exhibitionViewReader.show(owner.id(), viewWithSpirit("달리미"));

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.owner.friendCode").value(owner.code()))
                .andExpect(jsonPath("$.data.grid.width").value(20))
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("향로지기"))
                .andExpect(jsonPath("$.data.spirits[0].revealLevel").value("NAMED"))
                .andExpect(jsonPath("$.data.spirits[0].position.x").value(6))
                .andExpect(jsonPath("$.data.capturedAt").value(startsWith("2026-09-12T10:00")))
                .andExpect(jsonPath("$.data.giftState").value("AVAILABLE"))
                .andExpect(jsonPath("$.data.visitRewardAvailable").value(true));

        clock.advance(Duration.ofMinutes(5));
        get(owner, "/api/users/me").andExpect(status().isOk());

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(jsonPath("$.data.spirits[0].spiritName").value("달리미"))
                .andExpect(jsonPath("$.data.capturedAt").value(startsWith("2026-09-12T10:05")));
    }

    @Test
    @DisplayName("한 번도 접속하지 않은 친구의 전시관은 비어 있고, 친구가 아니면 볼 수 없다")
    void exhibitionWithoutSnapshotIsEmpty() throws Exception {
        Player owner = player("주인");
        Player visitor = player("방문객");
        Player stranger = player("낯선이");
        makeFriends(owner, visitor);

        get(visitor, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.spirits").isEmpty())
                .andExpect(jsonPath("$.data.buildings").isEmpty())
                .andExpect(jsonPath("$.data.grid").doesNotExist())
                .andExpect(jsonPath("$.data.capturedAt").doesNotExist());
        get(stranger, "/api/friends/{code}/exhibition", owner.code())
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F008"));
    }

    @Test
    @DisplayName("오늘의 추천은 같은 날 고정되고, 친구·차단 상대는 빠지며, 자정이 지나면 최근 추천하지 않은 사람을 먼저 뽑는다")
    void recommendationsAreFixedPerDayAndRenewAtMidnight() throws Exception {
        Player me = player("나");
        Player friend = player("친구");
        Player blocked = player("차단");
        makeFriends(me, friend);
        blockRepository.save(Block.of(me.id(), blocked.id()));
        List<Player> candidates = new ArrayList<>();
        for (int i = 0; i < 5; i++) {
            candidates.add(player("후보" + i));
        }
        for (Player active : allOf(friend, blocked, candidates)) {
            get(active, "/api/users/me").andExpect(status().isOk());
        }
        List<String> candidateCodes = candidates.stream().map(Player::code).toList();

        List<String> firstPicks = read(get(me, "/api/friends/recommendations")
                .andExpect(jsonPath("$.data.remaining").value(3))
                .andExpect(jsonPath("$.data.refreshesAt").value(startsWith("2026-09-13T00:00"))),
                "$.data.players[*].friendCode");
        assertThat(firstPicks).hasSize(3).doesNotContain(friend.code(), blocked.code());
        assertThat(candidateCodes).containsAll(firstPicks);

        List<String> samePicks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");
        assertThat(samePicks).containsExactlyElementsOf(firstPicks);

        sendRequest(me, firstPicks.get(0)).andExpect(status().isCreated());
        get(me, "/api/friends/recommendations")
                .andExpect(jsonPath("$.data.remaining").value(2))
                .andExpect(jsonPath("$.data.players[?(@.friendCode == '%s')].requested".formatted(firstPicks.get(0)))
                        .value(hasItem(true)));

        clock.set(NEXT_DAY);

        // 최근 추천하지 않은 후보는 2명뿐이라, 모자란 1명은 어제 추천했던 사람으로 채운다.
        // 어제 요청을 보낸 사람은 요청이 살아 있으므로 끝까지 제외된다.
        List<String> notYetRecommended = candidateCodes.stream()
                .filter(code -> !firstPicks.contains(code))
                .toList();
        List<String> nextDayPicks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");
        assertThat(nextDayPicks)
                .hasSize(3)
                .containsAll(notYetRecommended)
                .doesNotContain(firstPicks.get(0), friend.code(), blocked.code());
    }

    @Test
    @DisplayName("최근 접속자가 3명보다 적으면 오래 접속하지 않은 사람으로 채우되, 친구·차단 상대는 끝까지 뺀다")
    void fillsRecommendationsFromDormantUsersButNeverExcludedOnes() throws Exception {
        Player me = player("나");
        Player friend = player("친구");
        Player blocked = player("차단");
        Player active = player("최근접속");
        Player dormantOne = player("휴면하나");
        Player dormantTwo = player("휴면둘");
        makeFriends(me, friend);
        blockRepository.save(Block.of(blocked.id(), me.id()));

        clock.set(START.minusDays(30));
        get(dormantOne, "/api/users/me").andExpect(status().isOk());
        get(dormantTwo, "/api/users/me").andExpect(status().isOk());
        clock.set(START);
        for (Player recent : List.of(friend, blocked, active)) {
            get(recent, "/api/users/me").andExpect(status().isOk());
        }

        List<String> picks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");

        assertThat(picks).containsExactlyInAnyOrder(active.code(), dormantOne.code(), dormantTwo.code());
    }

    @Test
    @DisplayName("최근 7일 안에 추천하지 않은 사람이 모자라면, 최근 추천했던 사람 중 추천한 지 가장 오래된 사람부터 채운다")
    void fillsWithLongestAgoRecommendedWhenFreshCandidatesRunOut() throws Exception {
        Player me = player("나");
        Player fresh = player("처음");
        Player sixAndOneDaysAgo = player("엿새하루");
        Player threeDaysAgo = player("사흘");
        Player twoDaysAgo = player("이틀");
        Player oneDayAgo = player("하루");
        Player friendSevenDaysAgo = player("친구");
        makeFriends(me, friendSevenDaysAgo);
        recommendedDaysAgo(me, friendSevenDaysAgo, 7);
        recommendedDaysAgo(me, sixAndOneDaysAgo, 6);
        recommendedDaysAgo(me, threeDaysAgo, 3);
        recommendedDaysAgo(me, twoDaysAgo, 2);
        recommendedDaysAgo(me, sixAndOneDaysAgo, 1);
        recommendedDaysAgo(me, oneDayAgo, 1);

        List<String> picks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");

        // 친구는 가장 오래전에 추천했어도 제외 대상이고, 여러 번 추천한 사람은 마지막 추천(하루 전)을 기준으로 한다.
        assertThat(picks).containsExactly(fresh.code(), threeDaysAgo.code(), twoDaysAgo.code());
    }

    @Test
    @DisplayName("추천받은 사람과 차단 관계가 되면 목록에서 빠지고, 그 자리는 다른 사람으로 채우지 않는다")
    void doesNotRefillBlockedRecommendation() throws Exception {
        Player me = player("나");
        for (int i = 0; i < 4; i++) {
            player("후보" + i);
        }
        List<String> firstPicks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");
        assertThat(firstPicks).hasSize(3);

        postCode(me, "/api/blocks", firstPicks.get(0)).andExpect(status().isOk());

        List<String> afterBlock = read(get(me, "/api/friends/recommendations")
                .andExpect(jsonPath("$.data.remaining").value(2)), "$.data.players[*].friendCode");
        assertThat(afterBlock).containsExactlyElementsOf(firstPicks.subList(1, 3));
        assertThat(recommendationRepository.findAllByUserIdAndRecommendedDateOrderByIdAsc(me.id(), START.toLocalDate()))
                .hasSize(3);
    }

    @Test
    @DisplayName("처음 조회할 때 후보가 모자랐어도 같은 날 후보가 생기면 뽑아 둔 사람은 두고 3명까지 채운다")
    void topsUpRecommendationsWhenCandidatesAppearLater() throws Exception {
        Player me = player("나");
        Player early = player("먼저");

        List<String> firstPicks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");
        assertThat(firstPicks).containsExactly(early.code());

        Player lateOne = player("나중하나");
        Player lateTwo = player("나중둘");

        List<String> picks = read(get(me, "/api/friends/recommendations"), "$.data.players[*].friendCode");
        assertThat(picks).hasSize(3).startsWith(early.code()).contains(lateOne.code(), lateTwo.code());
    }

    @Test
    @DisplayName("연속 접속 일수는 매일 접속하면 오르고, 하루라도 빠지면 친구 목록에서 0으로 보인다")
    void streakFollowsDailyAccess() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        makeFriends(alice, bob);

        get(alice, "/api/users/me");
        clock.set(LocalDateTime.of(2026, 9, 13, 9, 0));
        get(alice, "/api/users/me");
        get(bob, "/api/friends")
                .andExpect(jsonPath("$.data.friends[0].streakDays").value(2))
                .andExpect(jsonPath("$.data.friends[0].lastActiveDaysAgo").value(0));

        clock.set(LocalDateTime.of(2026, 9, 15, 9, 0));
        get(bob, "/api/friends")
                .andExpect(jsonPath("$.data.friends[0].streakDays").value(0))
                .andExpect(jsonPath("$.data.friends[0].lastActiveDaysAgo").value(2));

        get(alice, "/api/users/me");
        get(bob, "/api/friends").andExpect(jsonPath("$.data.friends[0].streakDays").value(1));
    }

    private ExhibitionView viewWithSpirit(String spiritName) {
        return new ExhibitionView(
                FakeExhibitionViewReader.GRID,
                List.of(new ExhibitionView.SpiritView(
                        2, Era.THREE_KINGDOMS, "백제, 향이 스민 산맥", "금동대향로", spiritName,
                        AwakeningStage.AWAKENED_PLUS, new ExhibitionView.Position(6, 11))),
                List.of());
    }

    private void recommendedDaysAgo(Player owner, Player recommended, int daysAgo) {
        recommendationRepository.save(
                FriendRecommendation.of(owner.id(), recommended.id(), START.toLocalDate().minusDays(daysAgo)));
    }

    private static List<Player> allOf(Player first, Player second, List<Player> rest) {
        List<Player> all = new ArrayList<>(List.of(first, second));
        all.addAll(rest);
        return all;
    }
}
